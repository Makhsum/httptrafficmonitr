using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HttpTrafficMonitor.Models;
using Microsoft.Win32;

namespace HttpTrafficMonitor.ViewModels
{
    public class AutoResponderViewModel : ViewModelBase
    {
        private AutoResponderRule? _selectedRule;
        private bool _isEnabled = true;

        // New rule fields
        private string _newUrlPattern = string.Empty;
        private bool _newIsRegex;
        private string _newHttpMethod = "ANY";
        private int _newStatusCode = 200;
        private string _newResponseHeaders = "Content-Type: application/json";
        private string _newResponseBody = "{}";
        private string? _newResponseFilePath;
        private int _newDelayMs;

        public ObservableCollection<AutoResponderRule> Rules { get; }
        public static readonly string[] HttpMethods = { "ANY", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

        public AutoResponderRule? SelectedRule
        {
            get => _selectedRule;
            set
            {
                if (SetProperty(ref _selectedRule, value) && value != null)
                    LoadRuleToEditor(value);
            }
        }

        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        public string NewUrlPattern { get => _newUrlPattern; set => SetProperty(ref _newUrlPattern, value); }
        public bool NewIsRegex { get => _newIsRegex; set => SetProperty(ref _newIsRegex, value); }
        public string NewHttpMethod { get => _newHttpMethod; set => SetProperty(ref _newHttpMethod, value); }
        public int NewStatusCode { get => _newStatusCode; set => SetProperty(ref _newStatusCode, value); }
        public string NewResponseHeaders { get => _newResponseHeaders; set => SetProperty(ref _newResponseHeaders, value); }
        public string NewResponseBody { get => _newResponseBody; set => SetProperty(ref _newResponseBody, value); }
        public string? NewResponseFilePath { get => _newResponseFilePath; set => SetProperty(ref _newResponseFilePath, value); }
        public int NewDelayMs { get => _newDelayMs; set => SetProperty(ref _newDelayMs, value); }

        public ICommand AddRuleCommand { get; }
        public ICommand UpdateRuleCommand { get; }
        public ICommand RemoveRuleCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand ClearFileCommand { get; }

        public AutoResponderViewModel(ObservableCollection<AutoResponderRule> rules)
        {
            Rules = rules;

            AddRuleCommand = new RelayCommand(AddRule, () => !string.IsNullOrWhiteSpace(NewUrlPattern));
            UpdateRuleCommand = new RelayCommand(_ => UpdateRule(), _ => SelectedRule != null);
            RemoveRuleCommand = new RelayCommand(_ => RemoveRule(), _ => SelectedRule != null);
            BrowseFileCommand = new RelayCommand(BrowseFile);
            ClearFileCommand = new RelayCommand(() => NewResponseFilePath = null);
        }

        private void AddRule()
        {
            Rules.Add(new AutoResponderRule
            {
                UrlPattern = NewUrlPattern,
                IsRegex = NewIsRegex,
                HttpMethod = NewHttpMethod,
                ResponseStatusCode = NewStatusCode,
                ResponseHeaders = NewResponseHeaders,
                ResponseBody = NewResponseBody,
                ResponseFilePath = NewResponseFilePath,
                DelayMs = NewDelayMs,
            });
            ClearEditor();
        }

        private void UpdateRule()
        {
            if (SelectedRule == null) return;
            SelectedRule.UrlPattern = NewUrlPattern;
            SelectedRule.IsRegex = NewIsRegex;
            SelectedRule.HttpMethod = NewHttpMethod;
            SelectedRule.ResponseStatusCode = NewStatusCode;
            SelectedRule.ResponseHeaders = NewResponseHeaders;
            SelectedRule.ResponseBody = NewResponseBody;
            SelectedRule.ResponseFilePath = NewResponseFilePath;
            SelectedRule.DelayMs = NewDelayMs;
        }

        private void RemoveRule()
        {
            if (SelectedRule != null)
            {
                Rules.Remove(SelectedRule);
                ClearEditor();
            }
        }

        private void BrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|JSON Files (*.json)|*.json|HTML Files (*.html)|*.html|XML Files (*.xml)|*.xml"
            };
            if (dialog.ShowDialog() == true)
                NewResponseFilePath = dialog.FileName;
        }

        private void LoadRuleToEditor(AutoResponderRule rule)
        {
            NewUrlPattern = rule.UrlPattern;
            NewIsRegex = rule.IsRegex;
            NewHttpMethod = rule.HttpMethod;
            NewStatusCode = rule.ResponseStatusCode;
            NewResponseHeaders = rule.ResponseHeaders;
            NewResponseBody = rule.ResponseBody;
            NewResponseFilePath = rule.ResponseFilePath;
            NewDelayMs = rule.DelayMs;
        }

        private void ClearEditor()
        {
            NewUrlPattern = string.Empty;
            NewIsRegex = false;
            NewHttpMethod = "ANY";
            NewStatusCode = 200;
            NewResponseHeaders = "Content-Type: application/json";
            NewResponseBody = "{}";
            NewResponseFilePath = null;
            NewDelayMs = 0;
        }
    }
}
