using System.IO;

namespace DBLC;

public sealed class PaperDocumentState
{
    private readonly AppState _app;
    private readonly WorkspaceNotesStore _notesStore;
    private readonly PlayerSheetStore _playerStore;
    private CancellationTokenSource? _notesSave;
    private CancellationTokenSource? _playerSave;
    private readonly object _playerSaveSync = new();
    private readonly object _notesSaveSync = new();
    private Guid _campaignId;
    private bool _notesDirty;
    private bool _playerDirty;

    public string Notes { get; private set; }
    public string PlayerMarkdown { get; private set; }
    public bool PlayerPreview { get; private set; }
    public Guid CampaignId => _campaignId;
    public string NoteSaveStatus { get; private set; } = "Saved locally";
    public string PlayerSaveStatus { get; private set; } = "Saved locally";
    public bool NotesUnavailable { get; private set; }
    public bool PlayerUnavailable { get; private set; }
    public bool HasPreviousPlayerSheet => _playerStore.HasPrevious(_campaignId);
    public event Action? Changed;

    public string ReadPlayerMarkdown(Guid campaignId)
    {
        if (campaignId != _campaignId) return _playerStore.Load(campaignId, !_app.LegacyPaperFallbackDisabled && _app.Campaigns.FirstOrDefault()?.Id == campaignId);
        if (PlayerUnavailable) throw new IOException("The Player Sheet could not be read. Restore file access and retry loading it.");
        return PlayerMarkdown;
    }

    public string ReadWorkspaceNotes(Guid campaignId)
    {
        if (campaignId != _campaignId) return _notesStore.Load(campaignId, !_app.LegacyPaperFallbackDisabled && _app.Campaigns.FirstOrDefault()?.Id == campaignId);
        if (NotesUnavailable) throw new IOException("Workspace Notes could not be read. Restore file access and retry loading them.");
        return Notes;
    }

    public PaperDocumentState(AppState app, WorkspaceNotesStore notesStore, PlayerSheetStore playerStore)
    {
        _app = app;
        _notesStore = notesStore;
        _playerStore = playerStore;
        _campaignId = app.ActiveCampaignId;
        var legacy = !app.LegacyPaperFallbackDisabled && app.Campaigns.FirstOrDefault()?.Id == _campaignId;
        Notes = "";
        PlayerMarkdown = "";
        LoadPapers(legacy);
        app.BeforeCampaignChange = SaveAll;
        app.Changed += OnAppStateChanged;
    }

    private void OnAppStateChanged()
    {
        if (_campaignId == _app.ActiveCampaignId) return;
        if (!SaveAll()) return;
        _campaignId = _app.ActiveCampaignId;
        var legacy = !_app.LegacyPaperFallbackDisabled && _app.Campaigns.FirstOrDefault()?.Id == _campaignId;
        LoadPapers(legacy);
        Changed?.Invoke();
    }

    private void LoadPapers(bool legacy)
    {
        try { Notes = _notesStore.Load(_campaignId, legacy); NotesUnavailable = false; NoteSaveStatus = "Saved locally"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Notes = ""; NotesUnavailable = true; NoteSaveStatus = "Could not read notes; original file was not changed"; }
        try { PlayerMarkdown = _playerStore.Load(_campaignId, legacy); PlayerUnavailable = false; PlayerSaveStatus = "Saved locally"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { PlayerMarkdown = ""; PlayerUnavailable = true; PlayerSaveStatus = "Could not read sheet; original file was not changed"; }
        _notesDirty = _playerDirty = false;
    }

    public void RetryUnavailablePapers()
    {
        var legacy = !_app.LegacyPaperFallbackDisabled && _app.Campaigns.FirstOrDefault()?.Id == _campaignId;
        if (NotesUnavailable)
            try { Notes = _notesStore.Load(_campaignId, legacy); NotesUnavailable = false; NoteSaveStatus = "Saved locally"; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        if (PlayerUnavailable)
            try { PlayerMarkdown = _playerStore.Load(_campaignId, legacy); PlayerUnavailable = false; PlayerSaveStatus = "Saved locally"; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    public void SetNotes(string value)
    {
        if (NotesUnavailable) return;
        if (Notes == value) return;
        Notes = value;
        _notesDirty = true;
        NoteSaveStatus = "Saving…";
        Changed?.Invoke();
        ScheduleSave(isPlayer: false);
    }

    public void SetPlayerMarkdown(string value)
    {
        if (PlayerUnavailable) return;
        if (PlayerMarkdown == value) return;
        PlayerMarkdown = value;
        _playerDirty = true;
        PlayerSaveStatus = "Saving…";
        Changed?.Invoke();
        ScheduleSave(isPlayer: true);
    }

    public void ApplyApprovedPlayerUpdate(Guid campaignId, string expectedOriginal, string updatedMarkdown)
    {
        lock (_playerSaveSync)
        {
            if (campaignId == _campaignId && PlayerUnavailable) throw new IOException("The Player Sheet is unavailable; no update was applied.");
            var current = campaignId == _campaignId
                ? PlayerMarkdown
                : _playerStore.Load(campaignId, !_app.LegacyPaperFallbackDisabled && _app.Campaigns.FirstOrDefault()?.Id == campaignId);
            if (!string.Equals(current, expectedOriginal, StringComparison.Ordinal))
                throw new InvalidOperationException("The Player Sheet changed while this proposal was open. Cancel it and ask the GM to prepare an update from the current sheet.");
            if (campaignId == _campaignId)
            {
                _playerSave?.Cancel();
                _playerSave = null;
            }
            _playerStore.ReplaceWithBackup(campaignId, current, updatedMarkdown);
            if (campaignId == _campaignId)
            {
                PlayerMarkdown = updatedMarkdown;
                _playerDirty = false;
                PlayerSaveStatus = "Saved locally";
            }
        }
        if (campaignId != _campaignId) return;
        Changed?.Invoke();
    }

    public void RestorePreviousPlayerSheet()
    {
        lock (_playerSaveSync)
        {
            _playerSave?.Cancel();
            _playerSave = null;
            PlayerMarkdown = _playerStore.RestorePrevious(_campaignId);
            _playerDirty = false;
            PlayerSaveStatus = "Previous sheet restored";
        }
        Changed?.Invoke();
    }

    public void TogglePlayerPreview()
    {
        SavePlayerNow();
        PlayerPreview = !PlayerPreview;
        Changed?.Invoke();
    }

    public bool SaveAll()
    {
        var saved = true;
        lock (_notesSaveSync)
        {
            _notesSave?.Cancel();
            _notesSave = null;
            if (_notesDirty) try { _notesStore.Save(_campaignId, Notes); _notesDirty = false; NoteSaveStatus = "Saved locally"; }
            catch { saved = false; NoteSaveStatus = "Could not save notes — unsaved changes kept"; }
        }
        lock (_playerSaveSync)
        {
            _playerSave?.Cancel();
            _playerSave = null;
            if (_playerDirty) try { _playerStore.Save(_campaignId, PlayerMarkdown); _playerStore.ClearPrevious(_campaignId); _playerDirty = false; PlayerSaveStatus = "Saved locally"; }
            catch { saved = false; PlayerSaveStatus = "Could not save sheet — unsaved changes kept"; }
        }
        Changed?.Invoke();
        return saved;
    }

    private void SavePlayerNow()
    {
        lock (_playerSaveSync)
        {
            _playerSave?.Cancel();
            _playerSave = null;
            if (_playerDirty) try { _playerStore.Save(_campaignId, PlayerMarkdown); _playerStore.ClearPrevious(_campaignId); _playerDirty = false; PlayerSaveStatus = "Saved locally"; }
            catch { PlayerSaveStatus = "Could not save sheet — unsaved changes kept"; }
        }
    }

    private void ScheduleSave(bool isPlayer)
    {
        var next = new CancellationTokenSource();
        var campaignId = _campaignId;
        var text = isPlayer ? PlayerMarkdown : Notes;
        if (isPlayer)
        {
            lock (_playerSaveSync)
            {
                var previous = _playerSave;
                _playerSave = next;
                previous?.Cancel();
            }
        }
        else
        {
            lock (_notesSaveSync)
            {
                var previous = _notesSave;
                _notesSave = next;
                previous?.Cancel();
            }
        }
        _ = SaveAfterDelayAsync(isPlayer, next, campaignId, text);
    }

    private async Task SaveAfterDelayAsync(bool isPlayer, CancellationTokenSource save, Guid campaignId, string text)
    {
        try
        {
            await Task.Delay(450, save.Token);
            if (isPlayer)
            {
                lock (_playerSaveSync)
                {
                    if (ReferenceEquals(_playerSave, save))
                    {
                        try { _playerStore.Save(campaignId, text); _playerStore.ClearPrevious(campaignId); if (_campaignId == campaignId && PlayerMarkdown == text) { _playerDirty = false; PlayerSaveStatus = "Saved locally"; } }
                        catch { if (_campaignId == campaignId) PlayerSaveStatus = "Could not save sheet — unsaved changes kept"; }
                    }
                }
            }
            else
            {
                lock (_notesSaveSync) if (ReferenceEquals(_notesSave, save))
                {
                    try { _notesStore.Save(campaignId, text); if (_campaignId == campaignId && Notes == text) { _notesDirty = false; NoteSaveStatus = "Saved locally"; } }
                    catch { if (_campaignId == campaignId) NoteSaveStatus = "Could not save notes — unsaved changes kept"; }
                }
            }
            if (_campaignId == campaignId) Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (isPlayer)
            {
                lock (_playerSaveSync) if (ReferenceEquals(_playerSave, save)) _playerSave = null;
            }
            else { lock (_notesSaveSync) if (ReferenceEquals(_notesSave, save)) _notesSave = null; }
            save.Dispose();
        }
    }
}
