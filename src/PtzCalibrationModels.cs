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
        public int schemaVersion { get; set; } = 3;
        public bool calibrated { get; set; }
        public int panTravelMs { get; set; }
        public int tiltTravelMs { get; set; }
        public PtzPosition position { get; set; } = new();
        public PtzPosition? temporaryPosition { get; set; }
        public Dictionary<string, PtzPreset> presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, string> nativePresets { get; set; } = new();
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

    public sealed class PtzNativePresetRequest
    {
        public string? name { get; set; }
    }

    public sealed class PtzNativePresetEntry
    {
        public int slot { get; set; }
        public string name { get; set; } = "";
        public bool configured { get; set; }
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
        public bool calibrated { get; set; }
        public int panTravelMs { get; set; }
        public int tiltTravelMs { get; set; }
        public string positionType { get; set; } = "hard-stop-referenced";
        public string presetRecallMode { get; set; } = "rehome-first";
    }
}
