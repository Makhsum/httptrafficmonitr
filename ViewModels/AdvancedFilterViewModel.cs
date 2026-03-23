using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using HttpTrafficMonitor.Models;

namespace HttpTrafficMonitor.ViewModels
{
    public class AdvancedFilterViewModel : ViewModelBase
    {
        private string _logicOperator = "AND";
        private string _presetName = string.Empty;
        private FilterPreset? _selectedPreset;
        private FilterCondition? _selectedCondition;

        // New condition fields
        private string _newField = "URL";
        private string _newOperator = "Contains";
        private string _newValue = string.Empty;

        public ObservableCollection<FilterCondition> Conditions { get; } = new();
        public ObservableCollection<FilterPreset> Presets { get; } = new();

        public string[] AvailableFields => FilterCondition.Fields;
        public string[] AvailableOperators => FilterCondition.Operators;
        public static readonly string[] LogicOperators = { "AND", "OR" };

        public string LogicOp { get => _logicOperator; set => SetProperty(ref _logicOperator, value); }
        public string PresetName { get => _presetName; set => SetProperty(ref _presetName, value); }
        public FilterPreset? SelectedPreset { get => _selectedPreset; set { if (SetProperty(ref _selectedPreset, value) && value != null) LoadPreset(value); } }
        public FilterCondition? SelectedCondition { get => _selectedCondition; set => SetProperty(ref _selectedCondition, value); }
        public string NewField { get => _newField; set => SetProperty(ref _newField, value); }
        public string NewOperator { get => _newOperator; set => SetProperty(ref _newOperator, value); }
        public string NewValue { get => _newValue; set => SetProperty(ref _newValue, value); }

        public bool? DialogResult { get; set; }

        public ICommand AddConditionCommand { get; }
        public ICommand RemoveConditionCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand ClearCommand { get; }

        public event Action? Applied;

        public AdvancedFilterViewModel()
        {
            AddConditionCommand = new RelayCommand(AddCondition, () => !string.IsNullOrWhiteSpace(NewValue));
            RemoveConditionCommand = new RelayCommand(_ => RemoveCondition(), _ => SelectedCondition != null);
            SavePresetCommand = new RelayCommand(SavePreset, () => !string.IsNullOrWhiteSpace(PresetName) && Conditions.Count > 0);
            DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedPreset != null);
            ApplyCommand = new RelayCommand(() => Applied?.Invoke());
            ClearCommand = new RelayCommand(() =>
            {
                Conditions.Clear();
                Applied?.Invoke();
            });
        }

        public FilterPreset BuildCurrentFilter()
        {
            return new FilterPreset
            {
                Name = PresetName,
                LogicOperator = LogicOp,
                Conditions = Conditions.ToList()
            };
        }

        public bool EvaluateEntry(HttpRequestEntry entry)
        {
            if (Conditions.Count == 0) return true;
            var preset = BuildCurrentFilter();
            return preset.Evaluate(entry);
        }

        private void AddCondition()
        {
            Conditions.Add(new FilterCondition
            {
                Field = NewField,
                Operator = NewOperator,
                Value = NewValue
            });
            NewValue = string.Empty;
        }

        private void RemoveCondition()
        {
            if (SelectedCondition != null)
                Conditions.Remove(SelectedCondition);
        }

        private void SavePreset()
        {
            var existing = Presets.FirstOrDefault(p => p.Name == PresetName);
            if (existing != null) Presets.Remove(existing);

            Presets.Add(BuildCurrentFilter());
        }

        private void DeletePreset()
        {
            if (SelectedPreset != null)
                Presets.Remove(SelectedPreset);
        }

        private void LoadPreset(FilterPreset preset)
        {
            Conditions.Clear();
            foreach (var c in preset.Conditions)
                Conditions.Add(new FilterCondition { Field = c.Field, Operator = c.Operator, Value = c.Value });
            LogicOp = preset.LogicOperator;
            PresetName = preset.Name;
        }
    }
}
