using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace XboxMetroLauncher.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
	private readonly Func<object?, Task> _execute;

	private readonly Predicate<object?>? _canExecute;

	private bool _isRunning;

	public event EventHandler? CanExecuteChanged;

	public AsyncRelayCommand(Func<Task> execute)
		: this((object? _) => execute())
	{
	}

	public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter)
	{
		if (!_isRunning)
		{
			return _canExecute?.Invoke(parameter) ?? true;
		}
		return false;
	}

	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter))
		{
			return;
		}
		try
		{
			_isRunning = true;
			RaiseCanExecuteChanged();
			await _execute(parameter);
		}
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.LogException(ex, "AsyncRelayCommand");
            System.Windows.MessageBox.Show("The action could not be completed: " + ex.Message, "DashX360");
        }
		finally
		{
			_isRunning = false;
			RaiseCanExecuteChanged();
		}
	}

	public void RaiseCanExecuteChanged()
	{
		this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
