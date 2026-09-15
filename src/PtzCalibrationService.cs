using System.Text.Json;

namespace V380Decoder.src
{
    // Tracks only commands issued through this service. It never claims to read camera coordinates.
    public sealed class PtzCalibrationService
    {
        private const int MinDurationMs = 50;
        private const int MaxDurationMs = 10_000;
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
                    movementActive = activeMovement != null
                };
            }
        }

        public void Calibrate()
        {
            lock (stateLock)
            {
                state.position = new PtzPosition();
                SaveLocked();
            }
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
                : await MoveToAsync(target);
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
                : await MoveToAsync(new PtzPosition { pan = preset.pan, tilt = preset.tilt });
        }

        public async Task<PtzMoveResult> MoveAsync(PtzMoveRequest request)
        {
            if (!TryParseDirection(request.direction, out var direction))
                return Failure("Direction must be up, down, left, or right.");
            if (request.durationMs < MinDurationMs || request.durationMs > MaxDurationMs)
                return Failure($"durationMs must be between {MinDurationMs} and {MaxDurationMs}.");

            await movementLock.WaitAsync();
            try
            {
                var cancellation = new CancellationTokenSource();
                lock (stateLock) activeMovement = cancellation;
                try
                {
                    if (!Send(direction)) return Failure("The camera control connection is unavailable.");

                    bool cancelled = false;
                    try { await Task.Delay(request.durationMs, cancellation.Token); }
                    catch (OperationCanceledException) { cancelled = true; }

                    bool stopped = client.PtzStop();
                    if (!cancelled && stopped)
                    {
                        lock (stateLock)
                        {
                            ApplyMovementLocked(direction, request.durationMs);
                            SaveLocked();
                        }
                    }
                    return new PtzMoveResult
                    {
                        ok = stopped,
                        completed = !cancelled && stopped,
                        error = stopped ? null : "The camera did not accept the stop command.",
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

        private async Task<PtzMoveResult> MoveToAsync(PtzPosition target)
        {
            PtzPosition current = GetPosition();
            var horizontal = target.pan - current.pan;
            if (horizontal != 0)
            {
                var result = await MoveAsync(new PtzMoveRequest { direction = horizontal > 0 ? "right" : "left", durationMs = Math.Abs(horizontal) });
                if (!result.completed) return result;
            }
            current = GetPosition();
            var vertical = target.tilt - current.tilt;
            if (vertical != 0)
            {
                var result = await MoveAsync(new PtzMoveRequest { direction = vertical > 0 ? "up" : "down", durationMs = Math.Abs(vertical) });
                if (!result.completed) return result;
            }
            return new PtzMoveResult { ok = true, completed = true, position = GetPosition() };
        }

        private PtzPosition GetPosition()
        {
            lock (stateLock) return state.position.Copy();
        }

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

        private void ApplyMovementLocked(string direction, int durationMs)
        {
            if (direction == "left") state.position.pan -= durationMs;
            if (direction == "right") state.position.pan += durationMs;
            if (direction == "up") state.position.tilt += durationMs;
            if (direction == "down") state.position.tilt -= durationMs;
        }

        private PtzPersistentState Load()
        {
            try
            {
                if (!File.Exists(stateFile)) return new PtzPersistentState();
                return JsonSerializer.Deserialize(File.ReadAllText(stateFile), AppJsonSerializerContext.Default.PtzPersistentState) ?? new PtzPersistentState();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PTZ] Failed to load state: {ex.Message}");
                return new PtzPersistentState();
            }
        }

        private void SaveLocked()
        {
            try
            {
                var directory = Path.GetDirectoryName(stateFile);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temporary = stateFile + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(state, AppJsonSerializerContext.Default.PtzPersistentState));
                File.Move(temporary, stateFile, true);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[PTZ] Failed to save state: {ex.Message}"); }
        }
    }
}
