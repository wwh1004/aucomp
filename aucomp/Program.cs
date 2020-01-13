using System;
using System.Cli;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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
			SyncInfo[] syncInfos;
			bool[] exists;
			string[] filePaths;
			List<SyncInfo> newSyncInfos;

			settings = CommandLine.Parse<Settings>(args);
			if (!Directory.Exists(settings.OutputDirectory))
				Directory.CreateDirectory(settings.OutputDirectory);
			syncInfos = LoadSyncInfos(settings.OutputDirectory);
			Array.Sort(syncInfos);
			exists = new bool[syncInfos.Length];
			newSyncInfos = new List<SyncInfo>();
			filePaths = Directory.EnumerateFiles(settings.InputDirectory, "*", SearchOption.AllDirectories).ToArray();
			foreach (string filePath in filePaths) {
				SyncInfo syncInfo;
				int index;

				syncInfo = new SyncInfo(filePath, settings.InputDirectory);
				index = Array.BinarySearch(syncInfos, syncInfo);
				if (index >= 0) {
					exists[index] = true;
					if (!syncInfos[index].EqualsExactly(syncInfo)) {
						syncInfos[index] = syncInfo;
						Console.WriteLine("已更新： " + syncInfo.RelativeFilePath);
						Operate(settings, filePath, true);
					}
				}
				else {
					newSyncInfos.Add(syncInfo);
					Console.WriteLine("新加入： " + syncInfo.RelativeFilePath);
					Operate(settings, filePath, true);
				}
			}
			for (int i = 0; i < syncInfos.Length; i++) {
				if (exists[i])
					continue;
				Console.WriteLine("已删除： " + syncInfos[i].RelativeFilePath);
				Operate(settings, syncInfos[i].RelativeFilePath, false);
			}
			syncInfos = Enumerable.Range(0, syncInfos.Length).Where(i => exists[i]).Select(i => syncInfos[i]).Concat(newSyncInfos).ToArray();
			Array.Sort(syncInfos);
			SaveSyncInfos(settings.OutputDirectory, syncInfos);
		}

		private static void Operate(Settings settings, string filePath, bool copyOrDelete) {
			string newFilePath;
			string newDirectory;

			newFilePath = Path.Combine(settings.OutputDirectory, filePath.Substring(settings.InputDirectory.Length + 1));
			newDirectory = Path.GetDirectoryName(newFilePath);
			if (!Directory.Exists(newDirectory))
				Directory.CreateDirectory(newDirectory);
			if (IsAudioFile(filePath))
				newFilePath = Path.ChangeExtension(newFilePath, ".mp3");
			if (copyOrDelete) {
				if (IsAudioFile(filePath))
					CallFFmpeg(filePath, newFilePath, settings.Arguments);
				else if (IsLyricFile(filePath))
					File.WriteAllText(newFilePath, File.ReadAllText(filePath), _gbEncondig);
			}
			else if (File.Exists(newFilePath))
				File.Delete(newFilePath);
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

		private static SyncInfo[] LoadSyncInfos(string directory) {
			string filePath;
			byte[] buffer;
			SyncInfo[] syncInfos;

			filePath = Path.Combine(directory, ".aucomp");
			if (!File.Exists(filePath))
				return Array.Empty<SyncInfo>();
			buffer = File.ReadAllBytes(filePath);
			if (buffer.Length < 4)
				throw new InvalidDataException();
			using (MemoryStream stream = new MemoryStream(buffer)) {
				int count;

				count = stream.ReadByte() | stream.ReadByte() << 8 | stream.ReadByte() << 16 | stream.ReadByte() << 24;
				syncInfos = new SyncInfo[count];
				for (int i = 0; i < count; i++)
					syncInfos[i] = new SyncInfo(stream);
			}
			return syncInfos;
		}

		private static void SaveSyncInfos(string directory, SyncInfo[] syncInfos) {
			using (MemoryStream stream = new MemoryStream()) {
				int count;

				count = syncInfos.Length;
				stream.WriteByte((byte)count);
				stream.WriteByte((byte)(count >> 8));
				stream.WriteByte((byte)(count >> 16));
				stream.WriteByte((byte)(count >> 24));
				for (int i = 0; i < count; i++)
					syncInfos[i].WriteTo(stream);
				File.WriteAllBytes(Path.Combine(directory, ".aucomp"), stream.ToArray());
			}
		}

		[DebuggerDisplay("{FilePath}")]
		private sealed unsafe class SyncInfo : IComparable<SyncInfo>, IEquatable<SyncInfo> {
			public string RelativeFilePath = string.Empty;
			public DateTime CreationTimeUtc;
			public DateTime LastWriteTimeUtc;

			public SyncInfo(string filePath, string rootDirectory) {
				FileInfo fileInfo;

				fileInfo = new FileInfo(filePath);
				RelativeFilePath = fileInfo.FullName.Substring(rootDirectory.Length + 1);
				CreationTimeUtc = fileInfo.CreationTimeUtc;
				LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
			}

			public SyncInfo(Stream stream) {
				List<byte> buffer1;
				byte[] buffer2;

				buffer1 = new List<byte>(128);
				while (true) {
					int b;

					b = stream.ReadByte();
					if (b == 0)
						break;
					if (b == -1)
						throw new InvalidDataException();
					else
						buffer1.Add((byte)b);
				}
				RelativeFilePath = Encoding.UTF8.GetString(buffer1.ToArray());
				buffer2 = new byte[sizeof(DateTime)];
				fixed (void* p = buffer2) {
					stream.Read(buffer2, 0, 8);
					CreationTimeUtc = *(DateTime*)p;
					stream.Read(buffer2, 0, 8);
					LastWriteTimeUtc = *(DateTime*)p;
				}
				if (stream.ReadByte() != 0)
					throw new InvalidDataException();
			}

			public void WriteTo(Stream stream) {
				byte[] buffer;

				buffer = Encoding.UTF8.GetBytes(RelativeFilePath);
				stream.Write(buffer, 0, buffer.Length);
				stream.WriteByte(0);
				fixed (void* p = &CreationTimeUtc)
					for (int i = 0; i < sizeof(DateTime); i++)
						stream.WriteByte(((byte*)p)[i]);
				fixed (void* p = &LastWriteTimeUtc)
					for (int i = 0; i < sizeof(DateTime); i++)
						stream.WriteByte(((byte*)p)[i]);
				stream.WriteByte(0);
			}

			public int CompareTo(SyncInfo other) {
				return string.CompareOrdinal(RelativeFilePath, other.RelativeFilePath);
			}

			public bool Equals(SyncInfo other) {
				if (other is null)
					return false;
				if (ReferenceEquals(this, other))
					return true;
				return RelativeFilePath == other.RelativeFilePath && CreationTimeUtc == other.CreationTimeUtc && LastWriteTimeUtc == other.LastWriteTimeUtc;
			}

			public bool EqualsExactly(SyncInfo other) {
				if (other is null)
					return false;
				if (ReferenceEquals(this, other))
					return true;
				return RelativeFilePath == other.RelativeFilePath && CreationTimeUtc == other.CreationTimeUtc && LastWriteTimeUtc == other.LastWriteTimeUtc;
			}

			public override int GetHashCode() {
				return RelativeFilePath.GetHashCode();
			}
		}
	}
}
