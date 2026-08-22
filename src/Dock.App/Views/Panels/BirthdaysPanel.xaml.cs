using System.Windows.Controls;
using System.Windows.Input;

namespace Dock.App.Views.Panels;

public partial class BirthdaysPanel : UserControl
{
    public BirthdaysPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised by "Edit list...". The panel does not open the file itself for the same reason none
    /// of these panels launch anything: opening a document is a shell call, and a UserControl that
    /// makes one cannot be built or tested without one. The island forwards it to the App, which
    /// owns the store that knows where the file is.
    /// </summary>
    public event Action? EditRequested;

    private void OnEditClick(object sender, MouseButtonEventArgs e) => EditRequested?.Invoke();
}
