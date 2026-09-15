using System.Diagnostics;

namespace V380Decoder.src
{
    /// <summary>
    /// Adds position and preset semantics to cameras which only expose directional
    /// motor commands. Position is estimated from motor run time, so it must be
    /// calibrated against the camera's physical stops before absolute moves are used.
    /// </summary>
    public sealed class SoftwarePtzController : IDisposable
    {
        public const int MaximumPresets = 16;

        private readonly Action<PtzDirection> send;
        private readonly object sync = new();
        private readonly Dictionary<string, PtzPreset> presets = new();
        private readonly TimeSpan panTravelTime;
        private readonly TimeSpan tiltTravelTime;
        private CancellationTokenSource automaticMove;
        private PtzDirection direction = PtzDirection.Stop;
        private long movementStarted;
        private double pan;
        private double tilt;
        private bool calibrated;

        public SoftwarePtzController(Action<PtzDirection> send,
            TimeSpan? panTravelTime = null, TimeSpan? tiltTravelTime = null)
        {
            this.send = send ?? throw new ArgumentNullException(nameof(send));
            this.panTravelTime = ValidateTravelTime(panTravelTime ?? TimeSpan.FromSeconds(12), nameof(panTravelTime));
            this.tiltTravelTime = ValidateTravelTime(tiltTravelTime ?? TimeSpan.FromSeconds(6), nameof(tiltTravelTime));
        }

        public PtzStatus GetStatus()
        {
            lock (sync)
            {
                UpdatePosition();
                return new PtzStatus(pan, tilt, direction != PtzDirection.Stop, calibrated);
            }
        }

        public void Move(PtzDirection newDirection)
        {
            if (newDirection == PtzDirection.Stop)
            {
                Stop();
                return;
            }

            CancelAutomaticMove();
            lock (sync)
            {
                UpdatePosition();
                direction = newDirection;
                movementStarted = Stopwatch.GetTimestamp();
                send(newDirection);
            }
        }

        public void Stop()
        {
            CancelAutomaticMove();
            StopMotor();
        }

        public Task CalibrateAsync(CancellationToken cancellationToken = default) =>
            BeginAutomaticMove(async token =>
            {
                // Overshoot each configured travel time to reliably find both hard stops.
                await RunMotorAsync(PtzDirection.Left, panTravelTime * 1.1, token);
                await RunMotorAsync(PtzDirection.Down, tiltTravelTime * 1.1, token);
                lock (sync)
                {
                    pan = -1;
                    tilt = -1;
                    calibrated = true;
                }
                await MoveToCoreAsync(0, 0, token);
            }, cancellationToken);

        public Task MoveToAsync(double targetPan, double targetTilt, CancellationToken cancellationToken = default)
        {
            EnsureCalibrated();
            return BeginAutomaticMove(token => MoveToCoreAsync(
                Math.Clamp(targetPan, -1, 1), Math.Clamp(targetTilt, -1, 1), token), cancellationToken);
        }

        public PtzPreset SetPreset(string name, string token = null)
        {
            EnsureCalibrated();
            lock (sync)
            {
                UpdatePosition();
                token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token.Trim();
                if (!presets.ContainsKey(token) && presets.Count >= MaximumPresets)
                    throw new InvalidOperationException($"A maximum of {MaximumPresets} presets is supported.");

                var preset = new PtzPreset(token, string.IsNullOrWhiteSpace(name) ? token : name.Trim(), pan, tilt);
                presets[token] = preset;
                return preset;
            }
        }

        public IReadOnlyList<PtzPreset> GetPresets()
        {
            lock (sync) return presets.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        }

        public bool RemovePreset(string token)
        {
            lock (sync) return presets.Remove(token ?? string.Empty);
        }

        public Task GoToPresetAsync(string token, CancellationToken cancellationToken = default)
        {
            PtzPreset preset;
            lock (sync)
            {
                if (!presets.TryGetValue(token ?? string.Empty, out preset))
                    throw new KeyNotFoundException($"Unknown preset '{token}'.");
            }
            return MoveToAsync(preset.Pan, preset.Tilt, cancellationToken);
        }

        private Task BeginAutomaticMove(Func<CancellationToken, Task> operation, CancellationToken externalToken)
        {
            CancelAutomaticMove();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            lock (sync) automaticMove = cts;
            return ExecuteAutomaticMoveAsync(operation, cts);
        }

        private async Task ExecuteAutomaticMoveAsync(Func<CancellationToken, Task> operation, CancellationTokenSource cts)
        {
            try { await operation(cts.Token); }
            finally
            {
                bool ownsMotor;
                lock (sync)
                {
                    ownsMotor = ReferenceEquals(automaticMove, cts);
                    if (ownsMotor) automaticMove = null;
                }
                if (ownsMotor) StopMotor();
                cts.Dispose();
            }
        }

        private async Task MoveToCoreAsync(double targetPan, double targetTilt, CancellationToken token)
        {
            PtzStatus current = GetStatus();
            await RunAxisAsync(targetPan - current.Pan, panTravelTime, PtzDirection.Left, PtzDirection.Right, token);
            current = GetStatus();
            await RunAxisAsync(targetTilt - current.Tilt, tiltTravelTime, PtzDirection.Down, PtzDirection.Up, token);
            lock (sync) { pan = targetPan; tilt = targetTilt; }
        }

        private async Task RunAxisAsync(double delta, TimeSpan travel, PtzDirection negative,
            PtzDirection positive, CancellationToken token)
        {
            if (Math.Abs(delta) < 0.001) return;
            await RunMotorAsync(delta < 0 ? negative : positive, travel * (Math.Abs(delta) / 2), token);
        }

        private async Task RunMotorAsync(PtzDirection motorDirection, TimeSpan duration, CancellationToken token)
        {
            lock (sync)
            {
                direction = motorDirection;
                movementStarted = Stopwatch.GetTimestamp();
                send(motorDirection);
            }
            await Task.Delay(duration, token);
            StopMotor();
        }

        private void StopMotor()
        {
            lock (sync)
            {
                UpdatePosition();
                if (direction != PtzDirection.Stop) send(PtzDirection.Stop);
                direction = PtzDirection.Stop;
            }
        }

        private void UpdatePosition()
        {
            if (direction == PtzDirection.Stop || movementStarted == 0) return;
            double elapsed = Stopwatch.GetElapsedTime(movementStarted).TotalSeconds;
            if (direction is PtzDirection.Left or PtzDirection.Right)
                pan = Math.Clamp(pan + elapsed * 2 / panTravelTime.TotalSeconds * (direction == PtzDirection.Right ? 1 : -1), -1, 1);
            else
                tilt = Math.Clamp(tilt + elapsed * 2 / tiltTravelTime.TotalSeconds * (direction == PtzDirection.Up ? 1 : -1), -1, 1);
            movementStarted = Stopwatch.GetTimestamp();
        }

        private void CancelAutomaticMove()
        {
            CancellationTokenSource cts;
            lock (sync)
            {
                cts = automaticMove;
                automaticMove = null;
            }
            cts?.Cancel();
            if (cts != null) StopMotor();
        }

        private void EnsureCalibrated()
        {
            lock (sync)
                if (!calibrated) throw new InvalidOperationException("PTZ must be calibrated before using positions or presets.");
        }

        private static TimeSpan ValidateTravelTime(TimeSpan value, string name) =>
            value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name, "Travel time must be positive.");

        public void Dispose()
        {
            CancelAutomaticMove();
            StopMotor();
        }
    }

    public enum PtzDirection { Stop, Left, Right, Up, Down }
    public sealed record PtzStatus(double Pan, double Tilt, bool IsMoving, bool IsCalibrated);
    public sealed record PtzPreset(string Token, string Name, double Pan, double Tilt);
}
