using System;
using System.Windows.Input;

namespace X9AEditor
{
    class RelayCommand : ICommand
    {
        Action action;
        Func<bool> canExecuteEvaluator;

        public RelayCommand(Action action, Func<bool> canExecuteEvaluator)
        {
            this.action = action;
            this.canExecuteEvaluator = canExecuteEvaluator;
        }

        public RelayCommand(Action action)
            : this(action, null)
        {
        }

        public bool CanExecute(object parameter)
        {
            if (canExecuteEvaluator == null)
                return true;
            else
                return canExecuteEvaluator();
        }

        public void Execute(object parameter)
        {
            action();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    class RelayCommand<T> : ICommand
    {
        Action<T> action;
        Func<T, bool> canExecuteEvaluator;

        public RelayCommand(Action<T> action, Func<T, bool> canExecuteEvaluator)
        {
            this.action = action;
            this.canExecuteEvaluator = canExecuteEvaluator;
        }

        public RelayCommand(Action<T> action) : this(action, null)
        {
        }

        public bool CanExecute(object parameter)
        {
            if (canExecuteEvaluator == null)
                return true;
            else
                return canExecuteEvaluator((T)parameter);
        }

        public void Execute(object parameter)
        {
            action((T)parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
