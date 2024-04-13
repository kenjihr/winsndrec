using CommandLine;

namespace winsndrec
{
    internal class Program
    {
        class Options
        {
            [Option('o', "output", Default = "sound.wav", HelpText = "Output audio file name.")]
            public string? Output { get; set; }

            [Option('b', "bits-per-sample", Default = null, HelpText = "Audio bits per sample. (16 or 24 or 32)")]
            public int? BitsPerSample { get; set; }

            [Option('t', "truncate-silence", Default = null, HelpText = "Threshold in decibel(dB) to truncate silence. The value can be from -10 to -100.")]
            public float? TruncateSilence { get; set; }
        }

        static WASAPICapture? capture;

        static int Main(string[] args)
        {
            string? outputBaseFileName = null;
            int? bitsPerSample = null;
            float? truncateSilence = null;

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(parsed => {
                    outputBaseFileName = parsed.Output;
                    if (parsed.BitsPerSample != null && (parsed.BitsPerSample == 16 || parsed.BitsPerSample == 24 || parsed.BitsPerSample == 32))
                        bitsPerSample = parsed.BitsPerSample;
                    if (parsed.TruncateSilence != null && (parsed.TruncateSilence <= -10 && parsed.TruncateSilence >= -100))
                        truncateSilence = parsed.TruncateSilence;
                })
                .WithNotParsed(notParsed => {
                    if (notParsed.IsHelp() || notParsed.IsVersion())
                        Environment.Exit(1);
                    Console.WriteLine("Command Line parameters provided were not valid.");
                    Environment.Exit(1);
                });

            Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) => {
                if (capture != null)
                {
                    e.Cancel = true;
                    capture.Shutdown();
                }
            };

            Console.Error.WriteLine("Press CTRL+C to stop.");

            capture = new WASAPICapture(outputBaseFileName, bitsPerSample, truncateSilence);
            if (!capture.Start())
                return 1;
            capture.Wait();
            capture.Dispose();

            return 0;
        }
    }
}
