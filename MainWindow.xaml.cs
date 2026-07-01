using System.Windows;
using Kitware.VTK;
using MeshGenLite.Services;
using MeshGenLite.ViewModels;
using Wpf.Ui.Controls;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using MouseButtons = System.Windows.Forms.MouseButtons;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButton = System.Windows.Input.MouseButton;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MeshGenLite;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel viewModel;
    private vtkRenderWindowInteractor? interactor;
    private readonly Dictionary<vtkRenderWindowInteractor, MprPlane> mprInteractors = new();

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new ShellViewModel(new PipelineService(), new VtkViewportService());
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        viewModel.InitializeViewport(RenderControl);
        viewModel.InitializeMpr(AxialControl, SagittalControl, CoronalControl);

        WireMprInteractor(AxialControl, MprPlane.Axial);
        WireMprInteractor(SagittalControl, MprPlane.Sagittal);
        WireMprInteractor(CoronalControl, MprPlane.Coronal);

        interactor = RenderControl.RenderWindow.GetInteractor();
        if (interactor is not null)
        {
            interactor.MouseMoveEvt += OnVtkMouseMove;
            interactor.LeftButtonPressEvt += OnVtkLeftButtonPress;
            interactor.RightButtonPressEvt += OnVtkRightButtonPress;
        }

        RenderControl.MouseMove += OnRenderMouseMove;
        RenderControl.MouseDown += OnRenderMouseDown;
        ViewportHost.MouseMove += OnViewportHostMouseMove;
        ViewportHost.PreviewMouseDown += OnViewportHostMouseDown;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (interactor is not null)
        {
            interactor.MouseMoveEvt -= OnVtkMouseMove;
            interactor.LeftButtonPressEvt -= OnVtkLeftButtonPress;
            interactor.RightButtonPressEvt -= OnVtkRightButtonPress;
            interactor = null;
        }

        RenderControl.MouseMove -= OnRenderMouseMove;
        RenderControl.MouseDown -= OnRenderMouseDown;
        ViewportHost.MouseMove -= OnViewportHostMouseMove;
        ViewportHost.PreviewMouseDown -= OnViewportHostMouseDown;
        PreviewKeyDown -= OnPreviewKeyDown;

        foreach (var it in mprInteractors.Keys)
        {
            it.LeftButtonPressEvt -= OnMprLeftDown;
            it.MouseMoveEvt -= OnMprMouseMove;
            it.LeftButtonReleaseEvt -= OnMprLeftUp;
        }

        mprInteractors.Clear();
    }

    private void WireMprInteractor(RenderWindowControl control, MprPlane plane)
    {
        var it = control.RenderWindow.GetInteractor();
        if (it is null)
        {
            return;
        }

        mprInteractors[it] = plane;
        it.LeftButtonPressEvt += OnMprLeftDown;
        it.MouseMoveEvt += OnMprMouseMove;
        it.LeftButtonReleaseEvt += OnMprLeftUp;
    }

    private void OnMprLeftDown(vtkObject sender, vtkObjectEventArgs e) => DispatchMpr(sender, MprMousePhase.Down);

    private void OnMprMouseMove(vtkObject sender, vtkObjectEventArgs e) => DispatchMpr(sender, MprMousePhase.Move);

    private void OnMprLeftUp(vtkObject sender, vtkObjectEventArgs e) => DispatchMpr(sender, MprMousePhase.Up);

    private void DispatchMpr(vtkObject sender, MprMousePhase phase)
    {
        if (sender is not vtkRenderWindowInteractor it || !mprInteractors.TryGetValue(it, out var plane))
        {
            return;
        }

        var position = it.GetEventPosition();
        if (position is null || position.Length < 2)
        {
            return;
        }

        // Interactor coordinates are already VTK display space (bottom-up); no flip needed.
        viewModel.HandleMprMouse(plane, position[0], position[1], MouseButtons.Left, phase);
    }

    private void OnRenderMouseMove(object? sender, FormsMouseEventArgs e)
    {
        viewModel.HandleViewportMove(e.X, e.Y);
    }

    private void OnRenderMouseDown(object? sender, FormsMouseEventArgs e)
    {
        viewModel.HandleViewportMouseDown(e.X, e.Y, e.Button);
    }

    private void OnViewportHostMouseMove(object sender, WpfMouseEventArgs e)
    {
        var position = e.GetPosition(ViewportHost);
        viewModel.HandleViewportMove((int)position.X, (int)position.Y);
    }

    private void OnViewportHostMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        var position = e.GetPosition(ViewportHost);
        if (e.ChangedButton == WpfMouseButton.Left)
        {
            viewModel.HandleViewportMouseDown((int)position.X, (int)position.Y, MouseButtons.Left);
        }
        else if (e.ChangedButton == WpfMouseButton.Right)
        {
            viewModel.HandleViewportMouseDown((int)position.X, (int)position.Y, MouseButtons.Right);
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != WpfKey.Escape)
        {
            return;
        }

        viewModel.HandleEscape();
        e.Handled = true;
    }

    private void OnVtkMouseMove(vtkObject sender, vtkObjectEventArgs e)
    {
        var position = interactor?.GetEventPosition();
        if (position is null || position.Length < 2)
        {
            return;
        }

        viewModel.HandleViewportMove(position[0], ToHostY(position[1]));
    }

    private void OnVtkLeftButtonPress(vtkObject sender, vtkObjectEventArgs e)
    {
        var position = interactor?.GetEventPosition();
        if (position is null || position.Length < 2)
        {
            return;
        }

        viewModel.HandleViewportMouseDown(position[0], ToHostY(position[1]), MouseButtons.Left);
    }

    private void OnVtkRightButtonPress(vtkObject sender, vtkObjectEventArgs e)
    {
        var position = interactor?.GetEventPosition();
        if (position is null || position.Length < 2)
        {
            return;
        }

        viewModel.HandleViewportMouseDown(position[0], ToHostY(position[1]), MouseButtons.Right);
    }

    private int ToHostY(int vtkY)
    {
        var height = Math.Max(1, RenderControl.Height);
        return Math.Max(0, height - vtkY - 1);
    }
}
