using System;
using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Services;

public sealed class JsonStore : IJsonStore
{
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly string _rootPath;

	public JsonStore(string rootPath)
	{
		_rootPath = Path.GetFullPath(rootPath);
		Directory.CreateDirectory(_rootPath);
	}

	public async Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = GetContainedPath(fileName);
		if (!File.Exists(path))
		{
			return default(T);
		}
		SemaphoreSlim fileLock = GetFileLock(path);
		await fileLock.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			await using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			fileLock.Release();
		}
	}

	public async Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = GetContainedPath(fileName);
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		SemaphoreSlim fileLock = GetFileLock(path);
		await fileLock.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		string tempPath = path + ".tmp";
		try
		{
			await using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				await stream.FlushAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				stream.Flush(flushToDisk: true);
			}

			if (File.Exists(path))
			{
				string backupPath = path + ".bak";
				File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
				TryDelete(backupPath);
			}
			else
			{
				File.Move(tempPath, path);
			}
		}
		finally
		{
			TryDelete(tempPath);
			fileLock.Release();
		}
	}

	private string GetContainedPath(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
		{
			throw new ArgumentException("A relative file name inside the store is required.", nameof(fileName));
		}
		string root = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string path = Path.GetFullPath(Path.Combine(root, fileName));
		string rootPrefix = root + Path.DirectorySeparatorChar;
		if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The requested JSON path escapes the configured store root.");
		}
		return path;
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static SemaphoreSlim GetFileLock(string path)
	{
		return FileLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
	}
}
