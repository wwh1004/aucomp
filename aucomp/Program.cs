using System;
using System.Cli;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace aucomp {
	internal sealed class Settings {
		private string _inputDirectory;
		private string _outputDirectory;
		private string _arguments;

		[Argument("-i", IsRequired = true)]
		public string InputDirectory {
			get => _inputDirectory;
			set {
				if (!Directory.Exists(value))
					throw new DirectoryNotFoundException();

				_inputDirectory = Path.GetFullPath(value);
			}
		}

		[Argument("-o", IsRequired = true)]
		public string OutputDirectory {
			get => _outputDirectory;
			set => _outputDirectory = Path.GetFullPath(value);
		}

		[Argument("-a", IsRequired = true)]
		public string Arguments {
			get => _arguments;
			set {
				if (string.IsNullOrEmpty(value))
					throw new ArgumentNullException(nameof(value));

				_arguments = value;
			}
		}
	}

	internal static class Program {
		private static readonly string[] _audioExtensions = new[] { ".aac", ".ape", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma" };
		private static readonly string[] _lyricExtensions = new[] { ".lrc" };
		private static readonly Encoding _gbEncondig = CodePagesEncodingProvider.Instance.GetEncoding("GB18030");

		private static void Main(string[] args) {
			Settings settings;
			Queue<string> filePaths;
			Task[] tasks;

			settings = CommandLine.Parse<Settings>(args);
			if (!Directory.Exists(settings.OutputDirectory))
				Directory.CreateDirectory(settings.OutputDirectory);
			filePaths = new Queue<string>(Directory.EnumerateFiles(settings.InputDirectory, "*", SearchOption.AllDirectories));
			tasks = new Task[Environment.ProcessorCount];
			for (int i = 0; i < tasks.Length; i++)
				tasks[i] = Task.Run(() => {
					while (true) {
						string filePath;

						lock (((ICollection)filePaths).SyncRoot) {
							if (filePaths.Count == 0)
								break;
							filePath = filePaths.Dequeue();
						}
						Copy(settings, filePath);
					}
				});
			Task.WaitAll(tasks);
			Console.WriteLine("完成");
			Console.ReadKey(true);
		}

		private static void Copy(Settings settings, string filePath) {
			string newFilePath;
			string newDirectory;

			newFilePath = Path.Combine(settings.OutputDirectory, filePath.Substring(settings.InputDirectory.Length + 1));
			newDirectory = Path.GetDirectoryName(newFilePath);
			if (!Directory.Exists(newDirectory))
				Directory.CreateDirectory(newDirectory);
			if (IsAudioFile(filePath))
				CallFFmpeg(filePath, Path.ChangeExtension(newFilePath, ".mp3"), settings.Arguments);
			else if (IsLyricFile(filePath))
				File.WriteAllText(newFilePath, File.ReadAllText(filePath), _gbEncondig);
		}

		private static void CallFFmpeg(string input, string output, string arguments) {
			arguments = $"-i \"{input}\" {arguments} \"{output}\"";
			Console.WriteLine($"ffmpeg.exe {arguments}");
			using (Process process = new Process() {
				StartInfo = new ProcessStartInfo("ffmpeg.exe", arguments) {
					CreateNoWindow = false,
					UseShellExecute = true
				}
			}) {
				process.Start();
				process.WaitForExit();
				if (process.ExitCode != 0) {
					Console.WriteLine($"ExitCode: {process.ExitCode}");
					Console.ReadKey(true);
				}
			}
		}

		private static bool IsAudioFile(string filePath) {
			string extension;

			extension = Path.GetExtension(filePath);
			foreach (string audioExtension in _audioExtensions)
				if (string.Equals(extension, audioExtension, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		private static bool IsLyricFile(string filePath) {
			string extension;

			extension = Path.GetExtension(filePath);
			foreach (string lyricExtension in _lyricExtensions)
				if (string.Equals(extension, lyricExtension, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}
	}
}
