using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Editor.Security;

internal static class Program
{
    private const long MiB = 1024L * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("HOMOLOGACAO FISICA NAO CONCLUIDA: " + ex.Message);
            Console.Error.WriteLine("Nao habilite o processamento em producao.");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 22000)
            throw new PlatformNotSupportedException("Este procedimento exige Windows 11.");
        if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
                "true", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Um runner remoto do GitHub nao comprova o HD D: do computador.");
        int mediaMiB = 64;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--size-mib" && i + 1 < args.Length &&
                int.TryParse(args[++i], out int parsed))
                mediaMiB = parsed;
            else
                throw new ArgumentException("Parametro nao reconhecido. Use --size-mib 64.");
        }
        // WAV RIFF classic cannot safely represent more than 4 GiB.
        if (mediaMiB is < 1 or > 4095)
            throw new ArgumentOutOfRangeException(nameof(mediaMiB),
                "Teste WAV local aceita de 1 a 4095 MiB; nao equivale a 10 GiB.");

        var drive = new DriveInfo(@"D:\");
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            throw new IOException("D: deve ser um volume local fixo e disponivel.");
        long beforeBytes = drive.AvailableFreeSpace;
        if (beforeBytes < (mediaMiB * MiB * 2 + 1024L * MiB))
            throw new IOException("Falta espaco no D: para os arquivos de teste e margens.");

        string root = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (!File.Exists(Path.Combine(root, "EditorMultimidia.sln")))
            throw new DirectoryNotFoundException(
                "Execute tests\\homologar-d.cmd a partir do repositorio do editor.");

        string evidence = Path.Combine(root, "TestResults",
            "physical-windows11-hdd", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        string caseFolder = Path.Combine(@"D:\",
            "editor-poc0032-physical-" + Guid.NewGuid().ToString("N"));
        // Never clean or overwrite other D: folders, libraries or personal media.
        Directory.CreateDirectory(caseFolder);

        var report = new Dictionary<string, object?>
        {
            ["schema"] = "editor-physical-hdd-cmd-v1",
            ["timestampUtc"] = DateTime.UtcNow.ToString("o"),
            ["executedLocally"] = true,
            ["operatingSystem"] = RuntimeInformation.OSDescription,
            ["drive"] = @"D:\",
            ["driveTypeReported"] = drive.DriveType.ToString(),
            ["hddTechnologyConfirmed"] = false,
            ["hddVerificationNote"] = "Confirme que D: e HD mecanico em 'Otimizar Unidades' no Windows. DriveType Fixed nao prova HDD.",
            ["freeBytesBefore"] = beforeBytes,
            ["mediaMiB"] = mediaMiB,
            ["fixtureFolder"] = caseFolder,
            ["evidenceFolder"] = evidence,
            ["testExitCode"] = -1,
            ["testsPassed"] = false,
            ["fixtureSha256Unchanged"] = false,
            ["stagingFilesRemaining"] = -1,
            ["readyForProduction"] = false,
            ["readyForDistribution"] = false,
            ["remainingGates"] = new[] {
                "CVE nativa FFmpeg e licencas GPL",
                "Teste com arquivos de ate 10 GiB em contêiner compatível",
                "Validacao de operacoes reais de projeto e recuperacao transacional",
                "Confirmacao fisica do tipo HDD e metrica de I/O",
            }
        };
        string outputFile = Path.Combine(evidence, "physical-hdd-evidence.json");
        bool passed = false;
        try
        {
            string testWorker = Path.Combine(root, "TestResults", "sandbox-worker");
            if (!File.Exists(Path.Combine(testWorker, "Editor.Security.Worker.exe")))
                throw new FileNotFoundException(
                    "Worker nao publicado. Execute tests\\homologar-d.cmd.");
            PrepareCodecBundle(testWorker, Path.Combine(root, "src",
                "Editor.Timeline.Wpf", "bin", "Release", "net10.0-windows", "worker"), report);

            string fixture = Path.Combine(caseFolder, "hdd-physical-fixture.wav");
            Console.WriteLine("Criando WAV fisico de " + mediaMiB +
                " MiB em D:, sem alterar mídias existentes...");
            CreateActualWav(fixture, mediaMiB);
            string beforeHash = Sha256(fixture);
            report["fixtureSha256Before"] = beforeHash;
            report["fixtureBytes"] = new FileInfo(fixture).Length;

            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] {
                "test", @"tests\Editor.Timeline.Tests\Editor.Timeline.Tests.csproj",
                "--configuration", "Release", "--no-build",
                "--logger", "trx;LogFileName=physical.trx",
                "--results-directory", evidence,
                "--filter",
                "FullyQualifiedName~AppContainerSeekableFdTests" +
                "|FullyQualifiedName~WpfPackagedWorkerTests" +
                "|FullyQualifiedName~AppContainerRealMetadataTests" +
                "|FullyQualifiedName~AppContainerStageRecoveryTests.Given64MiBRegisteredWav_WhenRealFfprobeInspects_ThenOriginalRemainsAndStagingIsClean" +
                "|FullyQualifiedName~AppContainerStageRecoveryTests.GivenRealDecoderInAppContainer_WhenJobIsForceClosed_ThenBothProcessesStopAndInputIsCleaned"
            }) start.ArgumentList.Add(argument);
            start.Environment["TEMP"] = caseFolder;
            start.Environment["TMP"] = caseFolder;
            start.Environment["TMPDIR"] = caseFolder;

            Console.WriteLine("Executando casos de AppContainer no HD D:...");
            string logPath = Path.Combine(evidence, "physical-test-output.log");
            int exit;
            long elapsedMilliseconds;
            long peakWorkingSet;
            using (var log = new StreamWriter(logPath, append: false,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var child = Process.Start(start)
                   ?? throw new InvalidOperationException("Nao foi possivel iniciar dotnet test."))
            {
                var mutex = new object();
                void Log(string? line, bool error)
                {
                    if (line is null) return;
                    lock (mutex)
                    {
                        log.WriteLine((error ? "ERR " : "") + line);
                        log.Flush();
                        Console.WriteLine(line);
                    }
                }
                child.OutputDataReceived += (_, e) => Log(e.Data, false);
                child.ErrorDataReceived += (_, e) => Log(e.Data, true);
                var timer = Stopwatch.StartNew();
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                if (!child.WaitForExit(10 * 60 * 1000))
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit();
                    throw new TimeoutException("Testes excederam dez minutos; nenhum gate aprovado.");
                }
                child.WaitForExit();
                timer.Stop();
                exit = child.ExitCode;
                elapsedMilliseconds = timer.ElapsedMilliseconds;
                peakWorkingSet = child.PeakWorkingSet64;
            }
            report["testExitCode"] = exit;
            report["testsElapsedSeconds"] = Math.Round(elapsedMilliseconds / 1000d, 2);
            report["testRunnerPeakWorkingSetBytes"] = peakWorkingSet;
            (int total, int failures) = ReadTrx(Path.Combine(evidence, "physical.trx"));
            report["observedTrxCases"] = total;
            report["observedTrxFailures"] = failures;
            bool hashOk = string.Equals(beforeHash, Sha256(fixture),
                StringComparison.OrdinalIgnoreCase);
            report["fixtureSha256After"] = Sha256(fixture);
            report["fixtureSha256Unchanged"] = hashOk;
            int staged = Directory.EnumerateFiles(caseFolder, ".editorworker-*",
                SearchOption.AllDirectories).Count();
            report["stagingFilesRemaining"] = staged;
            passed = exit == 0 && total >= 8 && failures == 0 && hashOk && staged == 0;
            report["testsPassed"] = passed;
            Console.WriteLine(passed
                ? "TESTES SINTETICOS NO D: APROVADOS. POC-003.2 AINDA NAO HOMOLOGADA."
                : "TESTES NO D: FALHARAM. Consulte o log e o JSON; producao segue bloqueada.");
        }
        catch (Exception ex)
        {
            report["error"] = ex.ToString();
            Console.Error.WriteLine("FALHA: " + ex.Message);
        }
        finally
        {
            report["freeBytesAfter"] = new DriveInfo(@"D:\").AvailableFreeSpace;
            report["completedUtc"] = DateTime.UtcNow.ToString("o");
            report["readyForProduction"] = false;
            report["readyForDistribution"] = false;
            File.WriteAllText(outputFile,
                JsonSerializer.Serialize(report, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine("Relatorio: " + outputFile);
            Console.WriteLine("Arquivos de teste no D: preservados em: " + caseFolder);
            Console.WriteLine("Nao apague arquivos de biblioteca; revise apenas a pasta de teste.");
        }
        return passed ? 0 : 1;
    }

    private static void PrepareCodecBundle(string testing, string wpf,
        Dictionary<string, object?> report)
    {
        string codecDir = LocateCodecDirectory();
        report["codecSourceDirectory"] = codecDir;
        string? ffmpegBanner = ReadFirstVersionLine(Path.Combine(codecDir, "ffmpeg.exe"));
        string? ffprobeBanner = ReadFirstVersionLine(Path.Combine(codecDir, "ffprobe.exe"));
        if (ffmpegBanner is null || !ffmpegBanner.StartsWith("ffmpeg version 9.",
                StringComparison.OrdinalIgnoreCase) ||
            ffprobeBanner is null || !ffprobeBanner.StartsWith("ffprobe version 9.",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "FFmpeg e ffprobe devem ser versoes 9.x estaveis do mesmo diretorio.");
        report["ffmpegVersion"] = ffmpegBanner;
        report["ffprobeVersion"] = ffprobeBanner;

        Directory.CreateDirectory(testing);
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            string source = Path.Combine(codecDir, name);
            string target = Path.Combine(testing, name);
            File.Copy(source, target, overwrite: true);
            if (!string.Equals(Sha256(source), Sha256(target),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Hash do codec divergiu durante a copia: " + name);
            report[name + "Sha256"] = Sha256(target);
        }
        Directory.CreateDirectory(wpf);
        foreach (string file in Directory.EnumerateFiles(testing, "*",
                     SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(testing, file);
            string target = Path.Combine(wpf, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        string sid = WindowsAppContainerPreflight.DerivePackageSid();
        report["appContainerSid"] = sid;
        GrantExactSandboxSid(testing, sid);
        GrantExactSandboxSid(wpf, sid);
    }

    private static string LocateCodecDirectory()
    {
        string? custom = Environment.GetEnvironmentVariable("EDITOR_FFMPEG_DIR");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(custom))
            candidates.Add(custom);
        string choco = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData),
            "chocolatey", "lib", "ffmpeg", "tools");
        if (Directory.Exists(choco))
        {
            foreach (string probe in Directory.EnumerateFiles(choco, "ffprobe.exe",
                         SearchOption.AllDirectories))
                candidates.Add(Path.GetDirectoryName(probe)!);
        }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.GetFullPath(entry.Trim('"'))); }
            catch (Exception) { /* Ignore invalid PATH entries. */ }
        }
        foreach (string folder in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (folder.Contains("chocolatey\\bin", StringComparison.OrdinalIgnoreCase))
                continue; // Never copy Chocolatey shim launchers.
            if (File.Exists(Path.Combine(folder, "ffmpeg.exe")) &&
                File.Exists(Path.Combine(folder, "ffprobe.exe")))
                return folder;
        }
        throw new FileNotFoundException(
            "FFmpeg/ffprobe reais nao encontrados. Configure EDITOR_FFMPEG_DIR no CMD " +
            "para a pasta contendo os dois executaveis (nao os atalhos do Chocolatey).");
    }

    private static string? ReadFirstVersionLine(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-version");
        using var p = Process.Start(start)
            ?? throw new IOException("Nao foi possivel verificar versao de " + executable);
        string? line = p.StandardOutput.ReadLine();
        if (!p.WaitForExit(5000) || p.ExitCode != 0)
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            throw new IOException("Falha ao validar versao do codec: " + executable);
        }
        return line;
    }

    private static void GrantExactSandboxSid(string bundle, string sid)
    {
        var info = new ProcessStartInfo("icacls.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        info.ArgumentList.Add(bundle);
        info.ArgumentList.Add("/grant");
        info.ArgumentList.Add("*" + sid + ":(OI)(CI)RX");
        info.ArgumentList.Add("/T");
        info.ArgumentList.Add("/C");
        using var child = Process.Start(info)
            ?? throw new IOException("Nao foi possivel iniciar icacls.");
        string output = child.StandardOutput.ReadToEnd();
        if (!child.WaitForExit(90_000) || child.ExitCode != 0)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            throw new UnauthorizedAccessException(
                "Nao foi possivel autorizar somente o AppContainer no bundle. " +
                output[^Math.Min(1500, output.Length)..]);
        }
    }

    private static void CreateActualWav(string path, int sizeMiB)
    {
        long length = sizeMiB * MiB;
        using var file = new FileStream(path, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(file);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked((int)(length + 36)));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(8000); writer.Write(8000);
        writer.Write((short)1); writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(checked((int)length));
        byte[] buffer = new byte[1024 * 1024];
        Array.Fill(buffer, (byte)128);
        while (length > 0)
        {
            int chunk = (int)Math.Min(length, buffer.Length);
            writer.Write(buffer, 0, chunk);
            length -= chunk;
        }
        writer.Flush();
        file.Flush(flushToDisk: true);
    }

    private static string Sha256(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static (int Total, int Failed) ReadTrx(string file)
    {
        if (!File.Exists(file))
            throw new IOException("Relatorio TRX ausente; nao aprovar homologacao.");
        var document = XDocument.Load(file);
        var records = document.Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
        int failed = records.Count(element =>
            !string.Equals(element.Attribute("outcome")?.Value,
                "Passed", StringComparison.OrdinalIgnoreCase));
        return (records.Length, failed);
    }
}
