using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HttpTrafficMonitor.Helpers;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.Services;

namespace HttpTrafficMonitor.ViewModels
{
    public class AlertsViewModel : ViewModelBase
    {
        private readonly AlertService _alertService;
        private bool _soundEnabled = true;
        private AlertRule? _selectedRule;
        private AlertEvent? _selectedAlert;
        private int _alertCount;

        // New rule fields
        private string _newRuleName = string.Empty;
        private AlertRuleType _newRuleType = AlertRuleType.StatusCode;
        private string _newRulePattern = string.Empty;
        private int _newStatusMin = 500;
        private int _newStatusMax = 599;
        private int _newTimeThreshold = 5000;
        private long _newSizeThreshold = 1048576;
        private string? _newRuleError;

        public ObservableCollection<AlertRule> Rules => _alertService.Rules;
        public ObservableCollection<AlertEvent> AlertEvents { get; } = new();

        public AlertRule? SelectedRule { get => _selectedRule; set => SetProperty(ref _selectedRule, value); }
        public AlertEvent? SelectedAlert { get => _selectedAlert; set => SetProperty(ref _selectedAlert, value); }
        public int AlertCount { get => _alertCount; set => SetProperty(ref _alertCount, value); }

        public bool SoundEnabled
        {
            get => _soundEnabled;
            set { SetProperty(ref _soundEnabled, value); _alertService.SoundEnabled = value; }
        }

        public string NewRuleName { get => _newRuleName; set => SetProperty(ref _newRuleName, value); }
        public AlertRuleType NewRuleType { get => _newRuleType; set { if (SetProperty(ref _newRuleType, value)) NewRuleError = null; } }
        public string NewRulePattern { get => _newRulePattern; set { if (SetProperty(ref _newRulePattern, value)) NewRuleError = null; } }
        public int NewStatusMin { get => _newStatusMin; set => SetProperty(ref _newStatusMin, value); }
        public int NewStatusMax { get => _newStatusMax; set => SetProperty(ref _newStatusMax, value); }
        public int NewTimeThreshold { get => _newTimeThreshold; set => SetProperty(ref _newTimeThreshold, value); }
        public long NewSizeThreshold { get => _newSizeThreshold; set => SetProperty(ref _newSizeThreshold, value); }
        // Why the last Add was refused, or null when there is nothing to say
        public string? NewRuleError { get => _newRuleError; set => SetProperty(ref _newRuleError, value); }

        public AlertRuleType[] RuleTypes => Enum.GetValues<AlertRuleType>();

        public ICommand AddRuleCommand { get; }
        public ICommand RemoveRuleCommand { get; }
        public ICommand ClearAlertsCommand { get; }

        public AlertsViewModel(AlertService alertService)
        {
            _alertService = alertService;
            _alertService.AlertTriggered += OnAlertTriggered;

            AddRuleCommand = new RelayCommand(AddRule, () => !string.IsNullOrWhiteSpace(NewRuleName));
            RemoveRuleCommand = new RelayCommand(_ => RemoveRule(), _ => SelectedRule != null);
            ClearAlertsCommand = new RelayCommand(() =>
            {
                AlertEvents.Clear();
                AlertCount = 0;
            });

            if (_alertService.Rules.Count == 0)
                _alertService.AddDefaultRules();
        }

        private void OnAlertTriggered(AlertEvent e)
        {
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                AlertEvents.Insert(0, e);
                while (AlertEvents.Count > 500) AlertEvents.RemoveAt(AlertEvents.Count - 1);
                AlertCount = AlertEvents.Count;
            });
        }

        private void AddRule()
        {
            var rule = new AlertRule
            {
                Name = NewRuleName,
                Type = NewRuleType,
                Pattern = NewRulePattern,
                StatusCodeMin = NewStatusMin,
                StatusCodeMax = NewStatusMax,
                ResponseTimeThresholdMs = NewTimeThreshold,
                SizeThresholdBytes = NewSizeThreshold,
                PlaySound = SoundEnabled
            };

            string? cannotFire = AlertService.FindWhyRuleCannotFire(rule);
            if (cannotFire != null)
            {
                NewRuleError = $"Rule not added: {cannotFire}";
                return;
            }

            // Only Domain and Process rules read the pattern; the others would quietly watch the form's fixed values instead
            string? watched = rule.Type switch
            {
                AlertRuleType.StatusCode => $"status codes {NewStatusMin}-{NewStatusMax}",
                AlertRuleType.ResponseTime => $"responses slower than {NewTimeThreshold} ms",
                AlertRuleType.RequestSize => $"request bodies larger than {FormatHelper.FormatSize(NewSizeThreshold)}",
                AlertRuleType.ResponseSize => $"responses larger than {FormatHelper.FormatSize(NewSizeThreshold)}",
                _ => null
            };
            if (watched != null && !string.IsNullOrWhiteSpace(NewRulePattern))
            {
                NewRuleError = $"Rule not added: a {rule.Type} rule does not use the pattern '{NewRulePattern}'; it watches {watched}. " +
                               "Leave Pattern empty to add it.";
                return;
            }

            _alertService.Rules.Add(rule);
            NewRuleError = null;
            NewRuleName = string.Empty;
            NewRulePattern = string.Empty;
        }

        private void RemoveRule()
        {
            if (SelectedRule != null)
                _alertService.Rules.Remove(SelectedRule);
        }
    }
}
