using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ZeroTouch.UI.Navigation
{
    public partial class FocusItemViewModel
        : ObservableObject, IFocusableItem
    {
        public ICommand Command { get; }

        private readonly Action<FocusItemViewModel>? _onActivated;

        [ObservableProperty] private bool _isSelected;
        
        [ObservableProperty] private bool _isAnimating;

        public FocusItemViewModel(ICommand command, Action<FocusItemViewModel>? onActivated = null)
        {
            Command = command;
            _onActivated = onActivated;
        }

        public async void Activate()
        {
            _onActivated?.Invoke(this);

            IsAnimating = true;
            
            if (Command?.CanExecute(null) == true)
                Command.Execute(null);
            
            await Task.Delay(500);
            IsAnimating = false;
        }
    }
}
