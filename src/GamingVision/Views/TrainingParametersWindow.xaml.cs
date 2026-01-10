using System.Windows;
using GamingVision.Models;
using GamingVision.Services.Training;

namespace GamingVision.Views;

/// <summary>
/// Dialog window for configuring training parameters before starting training.
/// </summary>
public partial class TrainingParametersWindow : Window
{
    private readonly TrainingMode _mode;

    /// <summary>
    /// Gets the configured training parameters if the user clicked Start Training.
    /// </summary>
    public ModelTrainingParameters? Parameters { get; private set; }

    /// <summary>
    /// Gets whether the user confirmed to start training.
    /// </summary>
    public bool StartTrainingConfirmed { get; private set; }

    public TrainingParametersWindow(ModelTrainingParameters? existingParams, TrainingMode mode)
    {
        InitializeComponent();

        _mode = mode;

        // Use existing parameters or defaults based on training mode
        var parameters = existingParams?.Clone()
            ?? (mode == TrainingMode.FineTune
                ? ModelTrainingParameters.DefaultFineTune()
                : ModelTrainingParameters.DefaultFullRetrain());

        DataContext = parameters;

        // Update title based on mode
        Title = mode == TrainingMode.FineTune
            ? "Fine-Tune Training Parameters"
            : "Full Retrain Parameters";
    }

    private void StartTraining_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ModelTrainingParameters parameters)
        {
            // Validate parameters
            if (parameters.Epochs < 10 || parameters.Epochs > 1000)
            {
                MessageBox.Show(
                    "Epochs must be between 10 and 1000.",
                    "Invalid Parameter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (parameters.Batch < 0.1 || parameters.Batch > 1.0)
            {
                MessageBox.Show(
                    "Batch (GPU %) must be between 0.1 and 1.0.",
                    "Invalid Parameter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (parameters.Patience < 5 || parameters.Patience > 200)
            {
                MessageBox.Show(
                    "Patience must be between 5 and 200.",
                    "Invalid Parameter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (parameters.LearningRate < 0.00001 || parameters.LearningRate > 0.5)
            {
                MessageBox.Show(
                    "Learning rate must be between 0.00001 and 0.5.",
                    "Invalid Parameter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (parameters.Workers < 0 || parameters.Workers > 32)
            {
                MessageBox.Show(
                    "Workers must be between 0 and 32.",
                    "Invalid Parameter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Parameters = parameters;
            StartTrainingConfirmed = true;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StartTrainingConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = _mode == TrainingMode.FineTune
            ? ModelTrainingParameters.DefaultFineTune()
            : ModelTrainingParameters.DefaultFullRetrain();

        DataContext = defaults;
    }
}
