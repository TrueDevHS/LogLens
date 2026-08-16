using System.ComponentModel;
using System.Windows;
using LogLens.Core.Persistence;

namespace LogLens.App.Views;

public partial class EraseDataWindow : Window
{
    private readonly EraseConfirmationStateMachine _confirmation = new();

    public EraseDataWindow(string storageRoot)
    {
        InitializeComponent();
        StoragePathTextBox.Text = storageRoot;
        ShowStage();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult != true)
        {
            _confirmation.Cancel();
        }

        base.OnClosing(e);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmation.Stage == EraseConfirmationStage.StorageBoundary
            && StorageBoundaryCheckBox.IsChecked != true)
        {
            return;
        }

        if (_confirmation.MoveNext())
        {
            ShowStage();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmation.MoveBack())
        {
            ShowStage();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _confirmation.Cancel();
        DialogResult = false;
    }

    private void EraseButton_Click(object sender, RoutedEventArgs e)
    {
        _confirmation.SetPhrase(ConfirmationPhraseTextBox.Text);
        if (_confirmation.AuthorizeErase())
        {
            DialogResult = true;
        }
    }

    private void ConfirmationPhraseTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _confirmation.SetPhrase(ConfirmationPhraseTextBox.Text);
        EraseButton.IsEnabled = _confirmation.CanErase;
    }

    private void StorageBoundaryCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_confirmation.Stage == EraseConfirmationStage.StorageBoundary)
        {
            NextButton.IsEnabled = StorageBoundaryCheckBox.IsChecked == true;
        }
    }

    private void ShowStage()
    {
        int step = (int)_confirmation.Stage;
        StepText.Text = $"STEP {step} OF 4";
        StageOnePanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StageTwoPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StageThreePanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StageFourPanel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = step > 1;
        NextButton.Visibility = step < 4 ? Visibility.Visible : Visibility.Collapsed;
        EraseButton.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        switch (_confirmation.Stage)
        {
            case EraseConfirmationStage.WhatWillBeErased:
                StageTitleText.Text = "What will be erased";
                StageSubtitleText.Text =
                    "Review the local LogLens session and cache data included in this action.";
                NextButton.IsEnabled = true;
                NextButton.Focus();
                break;
            case EraseConfirmationStage.WhatWillBeKept:
                StageTitleText.Text = "What will remain untouched";
                StageSubtitleText.Text =
                    "Source logs, exported reports and unrelated files are outside the erase boundary.";
                NextButton.IsEnabled = true;
                NextButton.Focus();
                break;
            case EraseConfirmationStage.StorageBoundary:
                StageTitleText.Text = "Confirm the storage boundary";
                StageSubtitleText.Text =
                    "Verify the exact Windows local application-data location before continuing.";
                NextButton.IsEnabled = StorageBoundaryCheckBox.IsChecked == true;
                StorageBoundaryCheckBox.Focus();
                break;
            case EraseConfirmationStage.FinalPhrase:
                StageTitleText.Text = "Type the final confirmation";
                StageSubtitleText.Text =
                    "The final action stays disabled until the exact phrase is entered.";
                ConfirmationPhraseTextBox.Focus();
                break;
        }
    }
}
