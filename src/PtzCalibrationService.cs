using System.Text.Json;

namespace V380Decoder.src
{
    // Tracks only commands issued through this service. It never claims to read camera coordinates.
    public sealed class PtzCalibrationService
    {
        private const int HorizontalPulseIntervalMs = 250;
        private const int VerticalPulseIntervalMs = 100;
        private const int DurationIncrementMs = 250;
        private const int MinDurationMs = DurationIncrementMs;
        private const int MaxDurationMs = 10_000;
        private const int MinTravelMs = 1_000;
        private const int MaxTravelMs = 60_000;
        private const int HomingMarginMs = 1_000;
        private readonly V380Client client;
        private readonly string stateFile;
        private readonly object stateLock = new();
        private readonly SemaphoreSlim movementLock = new(1, 1);
        private PtzPersistentState state;
        private CancellationTokenSource? activeMovement;

        public PtzCalibrationService(V380Client client, string stateFile)
        {
            this.client = client;
            this.stateFile = stateFile;
            state = Load();
        }

        public PtzStatusResponse GetStatus()
        {
            lock (stateLock)
            {
                return new PtzStatusResponse
                {
                    position = state.position.Copy(),
                    temporaryPosition = state.temporaryPosition?.Copy(),
                    presets = state.presets.ToDictionary(
                        pair => pair.Key,
                        pair => new PtzPreset { pan = pair.Value.pan, tilt = pair.Value.tilt },
                        StringComparer.OrdinalIgnoreCase),
                    movementActive = activeMovement != null,
                    calibrated = state.calibrated,
                    panTravelMs = state.panTravelMs,
                    tiltTravelMs = state.tiltTravelMs
                };
            }
        }

        public List<PtzNativePresetEntry> GetNativePresets()
        {
            lock (stateLock)
            {
                return Enumerable.Range(1, 16).Select(slot => new PtzNativePresetEntry
                {
                    slot = slot,
                    name = state.nativePresets.TryGetValue(slot, out var name) ? name : "",
                    configured = state.nativePresets.ContainsKey(slot)
                }).ToList();
            }
        }

        public bool SaveNativePreset(int slot, string? name, out string? error)
        {
            if (!IsValidNativeSlot(slot))
            {
                error = "Preset slot must be between 1 and 16.";
                return false;
            }

            string resolvedName;
            lock (stateLock)
            {
                resolvedName = string.IsNullOrWhiteSpace(name)
                    ? state.nativePresets.GetValueOrDefault(slot, $"Preset {slot}")
                    : name.Trim();
            }
            if (!IsValidNativeName(resolvedName))
            {
                error = "Preset names must contain 1-64 characters and cannot contain control characters.";
                return false;
            }
            if (!client.PtzSetNativePreset(slot))
            {
                error = "The camera did not accept the native preset save command.";
                return false;
            }

            lock (stateLock)
            {
                state.schemaVersion = 3;
                state.nativePresets[slot] = resolvedName;
                if (!SaveLocked())
                {
                    error = "The camera slot was saved, but its local name could not be persisted.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        public bool RecallNativePreset(int slot, out string? error)
        {
            if (!IsValidNativeSlot(slot))
            {
                error = "Preset slot must be between 1 and 16.";
                return false;
            }
            bool recalled = client.PtzRecallNativePreset(slot);
            error = recalled ? null : "The camera control connection is unavailable.";
            return recalled;
        }

        public bool DeleteNativePreset(int slot, out string? error)
        {
            if (!IsValidNativeSlot(slot))
            {
                error = "Preset slot must be between 1 and 16.";
                return false;
            }
            lock (stateLock)
            {
                if (!state.nativePresets.Remove(slot))
                {
                    error = "That slot has no local name mapping.";
                    return false;
                }
                if (!SaveLocked())
                {
                    error = "The local name was removed from memory but could not be persisted.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        public async Task<PtzMoveResult> CalibrateAsync(int panTravelMs, int tiltTravelMs)
        {
            if (panTravelMs < MinTravelMs || panTravelMs > MaxTravelMs ||
                tiltTravelMs < MinTravelMs || tiltTravelMs > MaxTravelMs ||
                panTravelMs % DurationIncrementMs != 0 || tiltTravelMs % DurationIncrementMs != 0)
                return Failure($"Travel times must be between {MinTravelMs} and {MaxTravelMs} milliseconds and use {DurationIncrementMs} ms increments.");

            await movementLock.WaitAsync();
            try
            {
                using var cancellation = new CancellationTokenSource();
                lock (stateLock) activeMovement = cancellation;
                try
                {
                    if (!await RunCommandAsync("left", panTravelMs + HomingMarginMs, cancellation.Token) ||
                        !await RunCommandAsync("down", tiltTravelMs + HomingMarginMs, cancellation.Token))
                        return Failure("Calibration was stopped or the camera control connection is unavailable.");

                    lock (stateLock)
                    {
                        state.schemaVersion = 2;
                        state.calibrated = true;
                        state.panTravelMs = panTravelMs;
                        state.tiltTravelMs = tiltTravelMs;
                        state.position = new PtzPosition();
                        state.temporaryPosition = null;
                        state.presets.Clear();
                        SaveLocked();
                    }
                    return Success();
                }
                finally
                {
                    lock (stateLock)
                        if (ReferenceEquals(activeMovement, cancellation)) activeMovement = null;
                }
            }
            finally { movementLock.Release(); }
        }

        public bool SavePreset(string name, out string? error)
        {
            if (!IsValidName(name))
            {
                error = "Preset names must contain 1-64 letters, numbers, underscores, or hyphens.";
                return false;
            }

            lock (stateLock)
            {
                if (!state.calibrated)
                {
                    error = "Run hard-stop calibration before saving presets.";
                    return false;
                }
                state.presets[name] = new PtzPreset { pan = state.position.pan, tilt = state.position.tilt };
                SaveLocked();
                error = null;
                return true;
            }
        }

        public bool DeletePreset(string name)
        {
            lock (stateLock)
            {
                bool deleted = state.presets.Remove(name);
                if (deleted) SaveLocked();
                return deleted;
            }
        }

        public void SaveTemporaryPosition()
        {
            lock (stateLock)
            {
                state.temporaryPosition = state.position.Copy();
                SaveLocked();
            }
        }

        public async Task<PtzMoveResult> RestoreAsync()
        {
            PtzPosition? target;
            lock (stateLock) target = state.temporaryPosition?.Copy();
            return target == null
                ? Failure("No temporary position has been saved.")
                : await MoveToFromHomeAsync(target);
        }

        public async Task<PtzMoveResult> GoToPresetAsync(string name)
        {
            PtzPreset? preset;
            lock (stateLock)
            {
                state.presets.TryGetValue(name, out preset);
                preset = preset == null ? null : new PtzPreset { pan = preset.pan, tilt = preset.tilt };
            }
            return preset == null
                ? Failure($"Preset '{name}' was not found.")
                : await MoveToFromHomeAsync(new PtzPosition { pan = preset.pan, tilt = preset.tilt });
        }

        public async Task<PtzMoveResult> MoveAsync(PtzMoveRequest request)
        {
            if (!TryParseDirection(request.direction, out var direction))
                return Failure("Direction must be up, down, left, or right.");
            if (request.durationMs < MinDurationMs || request.durationMs > MaxDurationMs)
                return Failure($"durationMs must be between {MinDurationMs} and {MaxDurationMs}.");
            if (request.durationMs % DurationIncrementMs != 0)
                return Failure($"durationMs must use {DurationIncrementMs} ms increments.");

            await movementLock.WaitAsync();
            try
            {
                var cancellation = new CancellationTokenSource();
                lock (stateLock) activeMovement = cancellation;
                try
                {
                    bool completed = await RunCommandAsync(direction, request.durationMs, cancellation.Token);
                    if (completed)
                    {
                        lock (stateLock)
                        {
                            ApplyMovementLocked(direction, request.durationMs);
                            SaveLocked();
                        }
                    }
                    return new PtzMoveResult
                    {
                        ok = completed,
                        completed = completed,
                        error = completed ? null : "Movement was stopped or the camera control connection is unavailable.",
                        position = GetPosition()
                    };
                }
                finally
                {
                    lock (stateLock)
                    {
                        if (ReferenceEquals(activeMovement, cancellation)) activeMovement = null;
                    }
                    cancellation.Dispose();
                }
            }
            finally { movementLock.Release(); }
        }

        public PtzMoveResult Stop()
        {
            lock (stateLock) activeMovement?.Cancel();
            bool stopped = client.PtzStop();
            return new PtzMoveResult { ok = stopped, completed = false, error = stopped ? null : "The camera control connection is unavailable.", position = GetPosition() };
        }

        private async Task<PtzMoveResult> MoveToFromHomeAsync(PtzPosition target)
        {
            int panTravelMs;
            int tiltTravelMs;
            lock (stateLock)
            {
                if (!state.calibrated) return Failure("Run hard-stop calibration before recalling presets.");
                panTravelMs = state.panTravelMs;
                tiltTravelMs = state.tiltTravelMs;
            }

            await movementLock.WaitAsync();
            try
            {
                using var cancellation = new CancellationTokenSource();
                lock (stateLock) activeMovement = cancellation;
                try
                {
                    // Re-establish a physical reference before every recall. This makes
                    // native-app and other unobserved movement irrelevant.
                    if (!await RunCommandAsync("left", panTravelMs + HomingMarginMs, cancellation.Token) ||
                        !await RunCommandAsync("down", tiltTravelMs + HomingMarginMs, cancellation.Token))
                        return Failure("Re-home was stopped or the camera control connection is unavailable.");

                    lock (stateLock) state.position = new PtzPosition();

                    int pan = Math.Clamp(target.pan, 0, panTravelMs);
                    int tilt = Math.Clamp(target.tilt, 0, tiltTravelMs);
                    if (pan > 0 && !await RunCommandAsync("right", pan, cancellation.Token))
                        return Failure("Horizontal preset movement was stopped.");
                    if (tilt > 0 && !await RunCommandAsync("up", tilt, cancellation.Token))
                        return Failure("Vertical preset movement was stopped.");

                    lock (stateLock)
                    {
                        state.position = new PtzPosition { pan = pan, tilt = tilt };
                        SaveLocked();
                    }
                    return Success();
                }
                finally
                {
                    lock (stateLock)
                        if (ReferenceEquals(activeMovement, cancellation)) activeMovement = null;
                }
            }
            finally { movementLock.Release(); }
        }

        private async Task<bool> RunCommandAsync(string direction, int durationMs, CancellationToken token)
        {
            int pulseIntervalMs = direction is "up" or "down"
                ? VerticalPulseIntervalMs
                : HorizontalPulseIntervalMs;
            try
            {
                int remainingMs = durationMs;
                while (remainingMs > 0)
                {
                    if (!Send(direction))
                    {
                        client.PtzStop();
                        return false;
                    }
                    int delayMs = Math.Min(pulseIntervalMs, remainingMs);
                    await Task.Delay(delayMs, token);
                    remainingMs -= delayMs;
                }
            }
            catch (OperationCanceledException) { client.PtzStop(); return false; }
            return client.PtzStop();
        }

        private PtzPosition GetPosition()
        {
            lock (stateLock) return state.position.Copy();
        }

        private PtzMoveResult Success() => new() { ok = true, completed = true, position = GetPosition() };
        private static PtzMoveResult Failure(string error) => new() { ok = false, completed = false, error = error };

        private bool Send(string direction) => direction switch
        {
            "up" => client.PtzUp(), "down" => client.PtzDown(),
            "left" => client.PtzLeft(), "right" => client.PtzRight(), _ => false
        };

        private static bool TryParseDirection(string? value, out string direction)
        {
            direction = value?.Trim().ToLowerInvariant() ?? "";
            return direction is "up" or "down" or "left" or "right";
        }

        private static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
        private static bool IsValidNativeSlot(int slot) => slot is >= 1 and <= 16;
        private static bool IsValidNativeName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && !name.Any(char.IsControl);

        private void ApplyMovementLocked(string direction, int durationMs)
        {
            if (direction == "left") state.position.pan -= durationMs;
            if (direction == "right") state.position.pan += durationMs;
            if (direction == "up") state.position.tilt += durationMs;
            if (direction == "down") state.position.tilt -= durationMs;
            if (state.calibrated)
            {
                state.position.pan = Math.Clamp(state.position.pan, 0, state.panTravelMs);
                state.position.tilt = Math.Clamp(state.position.tilt, 0, state.tiltTravelMs);
            }
        }

        private PtzPersistentState Load()
        {
            try
            {
                if (!File.Exists(stateFile)) return new PtzPersistentState();
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(stateFile), AppJsonSerializerContext.Default.PtzPersistentState) ?? new PtzPersistentState();
                if (loaded.schemaVersion < 2)
                {
                    // Version 1 positions used an arbitrary origin and cannot be
                    // converted into hard-stop-referenced coordinates safely.
                    return new PtzPersistentState();
                }
                loaded.nativePresets ??= new Dictionary<int, string>();
                return loaded;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PTZ] Failed to load state: {ex.Message}");
                return new PtzPersistentState();
            }
        }

        private bool SaveLocked()
        {
            try
            {
                var directory = Path.GetDirectoryName(stateFile);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temporary = stateFile + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(state, AppJsonSerializerContext.Default.PtzPersistentState));
                File.Move(temporary, stateFile, true);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PTZ] Failed to save state: {ex.Message}");
                return false;
            }
        }
    }
}
