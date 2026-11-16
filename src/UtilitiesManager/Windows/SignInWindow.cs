using System.Windows;
using System.Windows.Controls;
using UtilitiesManager.GitHub;

namespace UtilitiesManager;

/// <summary>
/// Connexion GitHub par code d'appareil : le code s'affiche, GitHub s'ouvre dans le navigateur, la fenêtre se ferme
/// seule une fois l'accès accordé. Le jeton est gardé chiffré (DPAPI) par <see cref="GitHubAccount"/>.
/// </summary>
public sealed class SignInWindow : ThemedWindow
{
    private readonly GitHubAccount _account;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _code = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status;
    private DeviceCode? _device;

    public SignInWindow(GitHubAccount account)
    {
        _account = account;
        Title = $"{AppPaths.AppName} - {L.T("Sign in to GitHub")}";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _status = Ui.Muted(L.T("Requesting a code…"));
        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        content.Children.Add(new TextBlock { Text = L.T("Enter this code on the GitHub page, then allow access:"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_code);
        content.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Ui.TextButton(L.T("Copy and open GitHub"), Open, 180));
        buttons.Children.Add(Ui.TextButton(L.T("Cancel"), Close));
        content.Children.Add(buttons);
        Content = content;
        Loaded += async (_, _) => await SignInAsync();
        Closed += (_, _) => _cancel.Cancel();
    }

    private void Open()
    {
        if (_device is null)
            return;
        Clipboard.SetText(_device.UserCode);
        Ui.OpenExternal(_device.VerificationUri);
    }

    private async Task SignInAsync()
    {
        try
        {
            _device = await _account.RequestCodeAsync(_cancel.Token);
            _code.Text = _device.UserCode;
            _status.Text = L.T("Waiting for GitHub…");
            await _account.SignInAsync(_device, _cancel.Token);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc) when (exc is GitHubException or HttpRequestException)
        {
            Log.Error($"Connexion GitHub : {exc.Message}");
            _status.Text = L.T("Sign-in failed: {0}", exc.Message);
        }
    }
}
