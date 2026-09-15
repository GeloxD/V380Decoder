namespace V380Decoder.src
{
    public sealed class PtzPosition
    {
        public int pan { get; set; }
        public int tilt { get; set; }

        public PtzPosition Copy() => new() { pan = pan, tilt = tilt };
    }

    public sealed class PtzPreset
    {
        public int pan { get; set; }
        public int tilt { get; set; }
    }

    public sealed class PtzPersistentState
    {
        public PtzPosition position { get; set; } = new();
        public PtzPosition? temporaryPosition { get; set; }
        public Dictionary<string, PtzPreset> presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class PtzMoveRequest
    {
        public string direction { get; set; } = "";
        public int durationMs { get; set; }
    }

    public sealed class PtzPresetRequest
    {
        public string name { get; set; } = "";
    }

    public sealed class PtzMoveResult
    {
        public bool ok { get; set; }
        public bool completed { get; set; }
        public string? error { get; set; }
        public PtzPosition position { get; set; } = new();
    }

    public sealed class PtzStatusResponse
    {
        public PtzPosition position { get; set; } = new();
        public PtzPosition? temporaryPosition { get; set; }
        public Dictionary<string, PtzPreset> presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool movementActive { get; set; }
        public string positionType { get; set; } = "software-calibrated";
    }
}
