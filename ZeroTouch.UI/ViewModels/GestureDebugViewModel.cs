using ZeroTouch.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZeroTouch.UI.ViewModels
{
    public partial class GestureDebugViewModel : ViewModelBase
    {
        private readonly GestureWebSocketClient _wsClient = new();

        [ObservableProperty]
        private string _connectionStatus = "Disconnected";

        [ObservableProperty]
        private string _lastGesture = "None";

        [ObservableProperty]
        private double _confidence = 0.0;

        public GestureDebugViewModel()
        {
            _wsClient.OnMessageReceived += HandleGestureMessage;
        }

        private void HandleGestureMessage(string message)
        {
            try
            {
                var json = JsonDocument.Parse(message);
                if (json.RootElement.TryGetProperty("gesture", out var g))
                    LastGesture = g.GetString() ?? "unknown";
                if (json.RootElement.TryGetProperty("confidence", out var c))
                    Confidence = c.GetDouble();
            }
            catch
            {
                LastGesture = "(invalid data)";
            }
        }

        [RelayCommand]
        public async Task ConnectToBackendAsync()
        {
            ConnectionStatus = "Connecting...";
            await _wsClient.ConnectAsync("ws://localhost:8765");
            ConnectionStatus = "Connected";
        }

        [RelayCommand]
        public async Task DisconnectAsync()
        {
            await _wsClient.DisconnectAsync();
            ConnectionStatus = "Disconnected";
        }
    }
}
