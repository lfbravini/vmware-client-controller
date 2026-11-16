namespace HorizonWatchdog.FakeClient;

public sealed class MainForm : Form
{
    private readonly Button _hangButton;
    private readonly Label _status;
    private bool _hanging;

    public MainForm()
    {
        Text = "HorizonWatchdog.FakeClient";
        Width = 420;
        Height = 200;
        StartPosition = FormStartPosition.CenterScreen;

        _status = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Responsive — message loop pumping"
        };

        _hangButton = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Text = "Simulate UI hang (block UI thread)"
        };
        _hangButton.Click += (_, _) => ToggleHang();

        Controls.Add(_status);
        Controls.Add(_hangButton);
    }

    private void ToggleHang()
    {
        if (_hanging)
        {
            return;
        }

        _hanging = true;
        _status.Text = "HUNG — UI thread blocked (SendMessageTimeout should fail)";
        _hangButton.Enabled = false;
        // Block the UI thread so SendMessageTimeout(SMTO_ABORTIFHUNG) fails.
        Thread.Sleep(Timeout.Infinite);
    }
}
