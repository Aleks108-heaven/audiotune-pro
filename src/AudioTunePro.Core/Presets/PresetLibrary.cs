using AudioTunePro.Core.Models;

namespace AudioTunePro.Core.Presets;

/// <summary>
/// Built-in presets, including two tuned specifically for ASUS Vivobook's small,
/// bass-light 1-2W laptop speakers (which roll off hard below ~150 Hz, and tend
/// to sound thin/harsh in the 2-4 kHz range at higher volume) and a general
/// headphone profile for when the Vivobook is driving external headphones.
/// Band order matches <see cref="EqEngine.StandardBandFrequencies"/>:
/// 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 Hz.
/// </summary>
public static class PresetLibrary
{
    public static IReadOnlyList<Preset> BuiltIns { get; } = Build();

    private static EqEngine Make(double bass, double treble, double preamp, params double[] bandGains) =>
        MakeWithSurround(SurroundMode.Off, 0.5, bass, treble, preamp, bandGains);

    private static EqEngine MakeWithSurround(SurroundMode surround, double amount, double bass, double treble, double preamp, params double[] bandGains)
    {
        var engine = new EqEngine { BassDb = bass, TrebleDb = treble, PreampDb = preamp };
        engine.Surround.Mode = surround;
        engine.Surround.Amount = amount;
        for (int i = 0; i < engine.Bands.Count && i < bandGains.Length; i++)
            engine.Bands[i].GainDb = bandGains[i];
        return engine;
    }

    private static List<Preset> Build() => new()
    {
        new Preset
        {
            Name = "Flat",
            Description = "No coloration — reference response.",
            IsBuiltIn = true,
            Engine = Make(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        },
        new Preset
        {
            Name = "ASUS Vivobook Speakers",
            Description = "Tuned for the Vivobook's built-in speakers: compensates for the " +
                          "bass roll-off below ~150 Hz, tames a harsh 2-4 kHz peak common on " +
                          "small laptop drivers, and lifts presence for clarity at low volume.",
            IsBuiltIn = true,
            Engine = MakeWithSurround(SurroundMode.Auto, 0.4, bass: 3.5, treble: 1.5, preamp: -1.5,
                /*31*/ 4.0, /*62*/ 5.5, /*125*/ 4.0, /*250*/ 1.5, /*500*/ 0.0,
                /*1k*/ 0.0, /*2k*/ -2.0, /*4k*/ -2.5, /*8k*/ 1.5, /*16k*/ 1.0),
        },
        new Preset
        {
            Name = "ASUS Vivobook Headphones",
            Description = "Gentler baseline for headphones plugged into the Vivobook's jack — " +
                          "light bass lift and a smoothed treble for long listening sessions.",
            IsBuiltIn = true,
            Engine = MakeWithSurround(SurroundMode.Auto, 0.5, bass: 2.0, treble: 1.0, preamp: -1.0,
                /*31*/ 2.5, /*62*/ 2.5, /*125*/ 1.5, /*250*/ 0.5, /*500*/ 0.0,
                /*1k*/ 0.0, /*2k*/ -0.5, /*4k*/ -1.0, /*8k*/ 0.5, /*16k*/ 0.5),
        },
        new Preset
        {
            Name = "Bass Boost",
            Description = "Deep, punchy low end.",
            IsBuiltIn = true,
            Engine = Make(bass: 6.0, treble: 0.0, preamp: -2.0,
                6.0, 6.0, 4.5, 2.0, 0.5, 0.0, 0.0, 0.0, 0.0, 0.0),
        },
        new Preset
        {
            Name = "Vocal Boost",
            Description = "Pushes speech and vocals forward — great for podcasts and calls.",
            IsBuiltIn = true,
            Engine = Make(bass: -1.0, treble: 0.5, preamp: -0.5,
                -2.0, -1.5, -1.0, 1.0, 3.0, 3.5, 2.5, 1.0, 0.0, -1.0),
        },
        new Preset
        {
            Name = "Movie",
            Description = "Wide, cinematic balance with reinforced dialogue and low-end impact.",
            IsBuiltIn = true,
            Engine = Make(bass: 3.0, treble: 1.0, preamp: -1.5,
                3.5, 3.0, 1.5, 0.5, 1.5, 2.0, 1.0, 0.5, 1.0, 1.5),
        },
        new Preset
        {
            Name = "Gaming",
            Description = "Emphasizes footsteps and directional cues without burying explosions.",
            IsBuiltIn = true,
            Engine = Make(bass: 2.0, treble: 2.5, preamp: -1.5,
                2.0, 2.0, 0.5, 0.0, 0.5, 1.5, 2.5, 2.0, 2.5, 2.0),
        },
        new Preset
        {
            Name = "Podcast / Voice Call",
            Description = "Cuts rumble, sharpens intelligibility for speech-only content.",
            IsBuiltIn = true,
            Engine = Make(bass: -4.0, treble: 0.0, preamp: 0.5,
                -6.0, -4.0, -2.0, 0.5, 2.5, 3.0, 2.0, 0.5, -1.0, -2.0),
        },
        new Preset
        {
            Name = "Classical",
            Description = "Natural, open response with a gentle smile curve.",
            IsBuiltIn = true,
            Engine = Make(bass: 1.5, treble: 2.0, preamp: -0.5,
                2.0, 1.5, 0.5, 0.0, 0.0, 0.0, 0.5, 1.0, 1.5, 2.0),
        },
        new Preset
        {
            Name = "Rock",
            Description = "Punchy low end and biting highs, slightly scooped mids.",
            IsBuiltIn = true,
            Engine = Make(bass: 4.0, treble: 3.0, preamp: -2.0,
                4.0, 3.0, 1.0, -0.5, -1.0, 0.0, 1.0, 2.0, 3.0, 3.5),
        },
        new Preset
        {
            Name = "Pop",
            Description = "Bright and forward with tight bass.",
            IsBuiltIn = true,
            Engine = Make(bass: 2.0, treble: 2.0, preamp: -1.0,
                1.5, 2.0, 1.0, 0.5, 1.0, 1.5, 1.0, 0.5, 1.5, 2.0),
        },
        new Preset
        {
            Name = "Electronic",
            Description = "Deep sub-bass and crisp top end for electronic/dance music.",
            IsBuiltIn = true,
            Engine = Make(bass: 5.0, treble: 3.5, preamp: -2.5,
                5.5, 4.5, 2.0, 0.0, -1.0, 0.0, 1.0, 2.0, 3.5, 4.0),
        },
    };
}
