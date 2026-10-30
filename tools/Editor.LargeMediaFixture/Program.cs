using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const long MiB = 1024L * 1024;
    private const long GB = 1000L * 1000 * 1000;
    private const int BufferBytes = 4 * 1024 * 1024;
    private const int HeaderBytes = 80;

    public static int Main(string[] args)
    {
        try { return Generate(args); }
        catch (Exception e)
        {
            Console.Error.WriteLine("Fixture RF64 NAO GERADA: " + e.Message);
            return 1;
        }
    }

    private static int Generate(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Somente Windows 11.");
        bool ci = args.Length == 1 && args[0] == "--ci-smoke";
        if (ci && !string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
                "true", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Modo CI disponivel apenas no GitHub Actions.");
        long targetBytes;
        if (ci)
            targetBytes = 8 * MiB;
        else if (args.Length == 2 && args[0] == "--size-mib" &&
                 int.TryParse(args[1], out int sizeMiB) && sizeMiB is >= 8 and <= 4096)
            targetBytes = sizeMiB * MiB;
        else if (args.Length == 2 && args[0] == "--size-gb" && args[1] == "10")
            targetBytes = 10L * GB; // exactly 10,000,000,000 bytes
        else
            throw new ArgumentException(
                "Use --size-mib 64, --size-gb 10 ou --ci-smoke no GitHub Actions.");

        if (targetBytes <= HeaderBytes || (targetBytes - HeaderBytes) % 16 != 0)
            throw new InvalidOperationException("RF64 PCM precisa de amostras completas.");

        string baseRoot;
        if (ci)
            baseRoot = Path.Combine(Directory.GetCurrentDirectory(), "TestResults",
                "rf64-generator-smoke");
        else
        {
            if (Environment.OSVersion.Version.Build < 22000)
                throw new PlatformNotSupportedException("Windows 11 e exigido.");
            var disk = new DriveInfo(@"D:\");
            if (!disk.IsReady || disk.DriveType != DriveType.Fixed)
                throw new IOException("Unidade local D: nao disponivel.");
            // Importing the source into a different active test library may
            // make another full 10 GB copy; require that capacity in advance.
            if (disk.AvailableFreeSpace < targetBytes * 2 + 2L * GB)
                throw new IOException(
                    "Espaco insuficiente: o teste exige o dobro do tamanho " +
                    "para permitir importar uma copia na biblioteca, mais 2 GB de margem.");
            baseRoot = @"D:\EditorMultimidia\Testes\_originais";
        }

        string outputFolder = Path.Combine(baseRoot,
            "rf64-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputFolder);
        string finalFile = Path.Combine(outputFolder,
            $"rf64-{targetBytes}-bytes.wav");
        string partialFile = finalFile + ".partial";
        var report = new Dictionary<string, object?>
        {
            ["schema"] = "editor-synthetic-rf64-fixture-v1",
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["testOnly"] = true,
            ["actualMediaContent"] = false,
            ["format"] = "RF64 PCM 16-bit, 8 channels, 192000Hz",
            ["requestedFileSizeBytes"] = targetBytes,
            ["sourceFile"] = finalFile,
            ["finalized"] = false,
            ["sha256"] = null,
            ["physicalHddBenchmarkPassed"] = false,
            ["appContainerTestPassed"] = false
        };
        string reportPath = Path.Combine(outputFolder, "synthetic-rf64-evidence.json");
        try
        {
            const int channels = 8;
            const int sampleRate = 192000;
            const int bitsPerSample = 16;
            const int blockAlign = channels * bitsPerSample / 8;
            const int bytesPerSecond = sampleRate * blockAlign;
            long dataBytes = targetBytes - HeaderBytes;
            byte[] header = MakeRf64Header(targetBytes, dataBytes,
                sampleRate, channels, bitsPerSample, blockAlign, bytesPerSecond);

            var timer = Stopwatch.StartNew();
            byte[] payload = new byte[BufferBytes];
            // Deterministic high-entropy data makes filesystem compression
            // much less likely than a stream of zeros, without holding 10GB RAM.
            new Random(0x1257D).NextBytes(payload);
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                using (var stream = new FileStream(partialFile,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        BufferSize = 1024 * 1024,
                        Options = FileOptions.SequentialScan
                    }))
                {
                    stream.Write(header);
                    sha.AppendData(header);
                    long remaining = dataBytes;
                    long reportedGiB = 0;
                    while (remaining > 0)
                    {
                        int chunk = (int)Math.Min(payload.Length, remaining);
                        stream.Write(payload.AsSpan(0, chunk));
                        sha.AppendData(payload.AsSpan(0, chunk));
                        remaining -= chunk;
                        long written = targetBytes - remaining;
                        if (written / (1024L * MiB) > reportedGiB)
                        {
                            reportedGiB = written / (1024L * MiB);
                            Console.WriteLine($"Gravados {reportedGiB} GiB...");
                        }
                    }
                    stream.Flush(flushToDisk: true);
                }
                report["sha256"] = Convert.ToHexString(sha.GetHashAndReset());
            }
            File.Move(partialFile, finalFile); // only after a full successful write
            timer.Stop();
            report["generationSeconds"] = Math.Round(timer.Elapsed.TotalSeconds, 2);
            long actual = new FileInfo(finalFile).Length;
            report["actualFileSizeBytes"] = actual;
            if (actual != targetBytes)
                throw new InvalidDataException("Tamanho escrito nao corresponde ao solicitado.");

            // Check physical random access near start, middle and EOF, not
            // just FileInfo.Length or a sparse SetLength() reservation.
            using (var reader = new FileStream(finalFile, FileMode.Open,
                FileAccess.Read, FileShare.Read))
            {
                foreach (long pos in new[] {
                    (long)HeaderBytes,
                    HeaderBytes + (dataBytes / 2 / payload.Length) * payload.Length,
                    targetBytes - 4096
                })
                {
                    reader.Position = pos;
                    byte[] sample = new byte[4096];
                    reader.ReadExactly(sample);
                    int origin = (int)((pos - HeaderBytes) % payload.Length);
                    if (!sample.AsSpan().SequenceEqual(payload.AsSpan(origin, sample.Length)))
                        throw new InvalidDataException(
                            "Falha de integridade em leitura aleatoria no RF64.");
                }
            }
            report["randomReadsPassed"] = true;
            report["finalized"] = true;
            Console.WriteLine("Arquivo RF64 gerado e checado: " + finalFile);
            Console.WriteLine("SHA-256 do fluxo escrito: " + report["sha256"]);
            Console.WriteLine(
                "Atenção: geracao concluida, mas decodificacao e sandbox ainda NAO testados.");
            return 0;
        }
        catch (Exception ex)
        {
            report["errorType"] = ex.GetType().Name;
            Console.Error.WriteLine("Falha durante escrita/verificacao: " + ex.Message);
            return 1;
        }
        finally
        {
            report["completedUtc"] = DateTimeOffset.UtcNow.ToString("O");
            File.WriteAllText(reportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true
                }), new UTF8Encoding(false));
            Console.WriteLine("Relatorio: " + reportPath);
            Console.WriteLine("Nunca remova bibliotecas pessoais; revise somente a pasta gerada.");
        }
    }

    private static byte[] MakeRf64Header(long fullFileBytes, long dataBytes,
        int sampleRate, short channels, short bitsPerSample,
        short blockAlign, int byteRate)
    {
        using var buffer = new MemoryStream(capacity: HeaderBytes);
        using (var w = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("RF64"));
            w.Write(uint.MaxValue); // classic RIFF 32-bit placeholder
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("ds64"));
            w.Write((uint)28);
            w.Write((ulong)(fullFileBytes - 8)); // 64-bit RIFF size
            w.Write((ulong)dataBytes);          // 64-bit data size
            w.Write((ulong)(dataBytes / blockAlign)); // 64-bit sample frames
            w.Write((uint)0); // no additional chunk-size records
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write((uint)16);
            w.Write((ushort)1); // PCM integer samples
            w.Write((ushort)channels);
            w.Write((uint)sampleRate);
            w.Write((uint)byteRate);
            w.Write((ushort)blockAlign);
            w.Write((ushort)bitsPerSample);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(uint.MaxValue); // extended size in ds64
        }
        byte[] head = buffer.ToArray();
        if (head.Length != HeaderBytes)
            throw new InvalidOperationException("RF64 header size mismatch.");
        return head;
    }
}
