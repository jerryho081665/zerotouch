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
        public object? CommandParameter { get; }
        
        public bool IsTwoStage { get; }

        private readonly Action<FocusItemViewModel>? _onActivated;
        private readonly Action<object?>? _onPreview;

        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isAnimating;
        [ObservableProperty] private bool _isArmed;

        public FocusItemViewModel(
            ICommand command,
            object? parameter = null,
            Action<FocusItemViewModel>? onActivated = null,
            Action<object?>? onPreview = null,
            bool isTwoStage = false)
        {
            Command = command;
            CommandParameter = parameter;
            _onActivated = onActivated;
            _onPreview = onPreview;
            IsTwoStage = isTwoStage;
        }

        public async void Activate()
        {
            _onActivated?.Invoke(this);
            
            if (!IsTwoStage)
            {
                ExecuteCommand();
                return;
            }
            
            if (!IsArmed)
            {
                IsArmed = true; 
                _onPreview?.Invoke(CommandParameter);
            }
            else
            {
                IsAnimating = true;
                
                await Task.Delay(500); 
                
                if (Command?.CanExecute(CommandParameter) == true)
                {
                    Command.Execute(CommandParameter);
                }
                
                await Task.Delay(200);
        
                IsAnimating = false;
                IsArmed = false;
            }
        }
        
        private async void ExecuteCommand()
        {
            IsAnimating = true;
            if (Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
            }
            await Task.Delay(500);
            IsAnimating = false;
        }
        
        partial void OnIsSelectedChanged(bool value)
        {
            if (!value) IsArmed = false;
        }
    }
}
