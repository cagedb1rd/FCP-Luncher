using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.Core.Accounts;

namespace FCP.App.ViewModels;

/// <summary>对话框的基类：外壳负责显示和关闭。</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _closed = new();

    public Task<bool> Closed => _closed.Task;

    public event EventHandler? CloseRequested;

    /// <summary>按 Esc 或点击遮罩时关闭。</summary>
    public virtual void Dismiss() => Close(false);

    protected void Close(bool result)
    {
        _closed.TrySetResult(result);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record DialogButton(string Text, bool IsPrimary, Action? Action = null);

/// <summary>账号对话框中的一行。</summary>
public sealed record AccountRow(AccountRecord Account, bool IsCurrent)
{
    public string PlayerName => Account.PlayerName;
    public string KindText => Account.KindText;
}

/// <summary>消息对话框：标题、说明、可选的详细信息（例如日志末尾），以及最多 3 个按钮。</summary>
public sealed partial class MessageDialogViewModel : DialogViewModel
{
    public MessageDialogViewModel(string title, string message, string? details = null, bool isError = false, params DialogButton[] buttons)
    {
        Title = title;
        Message = message;
        Details = details;
        IsError = isError;
        Buttons = buttons.Length > 0 ? buttons : [new DialogButton("知道了", true)];
    }

    public string Title { get; }
    public string Message { get; }
    public string? Details { get; }
    public bool IsError { get; }
    public bool HasDetails => !string.IsNullOrEmpty(Details);

    /// <summary>详细信息是否用等宽字体（日志用；普通说明文字设为 false）。</summary>
    public bool DetailsMonospace { get; init; } = true;
    public IReadOnlyList<DialogButton> Buttons { get; }

    [RelayCommand]
    private void Press(DialogButton button)
    {
        button.Action?.Invoke();
        Close(button.IsPrimary);
    }
}

/// <summary>账号对话框（P1：离线账号）：切换、删除已有账号，或添加新的离线账号。</summary>
public sealed partial class AccountDialogViewModel : DialogViewModel
{
    private readonly LauncherState _state;

    public AccountDialogViewModel(LauncherState state)
    {
        _state = state;
        // 检测到 PCL 时默认使用与 PCL 相同的离线 UUID，保证存档进度连续（docs/02 §6）
        _usePclUuid = state.HasPcl;
    }

    public IReadOnlyList<AccountRow> Accounts => _state.Accounts.Select(a => new AccountRow(a, a.Id == _state.Account?.Id)).ToList();
    public bool HasAccounts => _state.Accounts.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = "";

    [ObservableProperty]
    private bool _usePclUuid;

    public string? NameError => NewName.Length == 0 ? null : AccountService.ValidateOfflineName(NewName);

    private bool CanAdd() => NewName.Length > 0 && AccountService.ValidateOfflineName(NewName) is null;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        await _state.AddOfflineAccountAsync(NewName.Trim(), UsePclUuid ? UuidScheme.PclLegacy : UuidScheme.Standard);
        Close(true);
    }

    [RelayCommand]
    private async Task SelectAsync(AccountRow row)
    {
        await _state.SelectAccountAsync(row.Account);
        Close(true);
    }

    [RelayCommand]
    private async Task RemoveAsync(AccountRow row)
    {
        await _state.RemoveAccountAsync(row.Account);
        OnPropertyChanged(nameof(Accounts));
        OnPropertyChanged(nameof(HasAccounts));
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
