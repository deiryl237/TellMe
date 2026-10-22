using System.ComponentModel; 
using System.Runtime.CompilerServices;


namespace TellMe.App.ViewModels;

public class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
    {
    if (EqualityComparer<T>.Default.Equals(backingStore, value))
        return false;

    backingStore = value;
    OnPropertyChanged(propertyName);
    return true;
    }


    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
}
