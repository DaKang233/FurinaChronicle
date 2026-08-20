using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using Microsoft.Maui.Storage;

namespace FurinaChronicle.App.Persistence;

public sealed class PreferencesArchiveSelectionStore(IPreferences preferences)
	: IArchiveSelectionStore
{
	private const string CurrentSelectionKey = "archive-selection-v1";
	private const string ArchiveSelectionKeyPrefix = "archive-selection-v1.archive.";

	public Task<ArchiveSelection?> LoadAsync(
		CancellationToken cancellationToken = default)
	{
		return LoadByKeyAsync(CurrentSelectionKey, cancellationToken);
	}

	public Task<ArchiveSelection?> LoadForArchiveAsync(
		Guid playerArchiveId,
		CancellationToken cancellationToken = default)
	{
		if (playerArchiveId == Guid.Empty)
		{
			throw new ArgumentException(
				"玩家档案 ID 不能为空。",
				nameof(playerArchiveId));
		}

		return LoadByKeyAsync(
			GetArchiveSelectionKey(playerArchiveId),
			cancellationToken);
	}

	public Task SaveAsync(
		ArchiveSelection selection,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);
		cancellationToken.ThrowIfCancellationRequested();

		if (selection.PlayerArchiveId == Guid.Empty)
		{
			throw new ArgumentException(
				"玩家档案 ID 不能为空。",
				nameof(selection));
		}
		if (selection.GameAccountId == Guid.Empty)
		{
			throw new ArgumentException(
				"游戏账号 ID 不能为空。",
				nameof(selection));
		}

		string serialized = Serialize(selection);
		preferences.Set(CurrentSelectionKey, serialized);
		preferences.Set(
			GetArchiveSelectionKey(selection.PlayerArchiveId),
			serialized);
		return Task.CompletedTask;
	}

	public Task ClearAsync(
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		preferences.Remove(CurrentSelectionKey);
		return Task.CompletedTask;
	}

	private Task<ArchiveSelection?> LoadByKeyAsync(
		string key,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string serialized = preferences.Get(key, string.Empty);
		return Task.FromResult(Parse(serialized));
	}

	private static string GetArchiveSelectionKey(Guid playerArchiveId)
	{
		return $"{ArchiveSelectionKeyPrefix}{playerArchiveId:D}";
	}

	private static string Serialize(ArchiveSelection selection)
	{
		return $"{selection.PlayerArchiveId:D}|{selection.GameAccountId:D}";
	}

	private static ArchiveSelection? Parse(string serialized)
	{
		if (string.IsNullOrWhiteSpace(serialized))
		{
			return null;
		}

		string[] parts = serialized.Split(
			'|',
			count: 2,
			StringSplitOptions.None);

		if (parts.Length != 2 ||
			!Guid.TryParse(parts[0], out Guid archiveId) ||
			!Guid.TryParse(parts[1], out Guid accountId) ||
			archiveId == Guid.Empty ||
			accountId == Guid.Empty)
		{
			return null;
		}

		return new ArchiveSelection(archiveId, accountId);
	}
}