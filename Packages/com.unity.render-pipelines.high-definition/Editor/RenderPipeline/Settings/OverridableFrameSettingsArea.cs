using System;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Reflection;
using System.Linq;

namespace UnityEditor.Rendering.HighDefinition
{
    internal struct OverridableFrameSettingsArea
    {
        static readonly GUIContent overrideTooltip = EditorGUIUtility.TrTextContent("", "Override this setting in component.");
        static readonly Dictionary<FrameSettingsField, FrameSettingsFieldAttribute> attributes;
        static Dictionary<int, IOrderedEnumerable<KeyValuePair<FrameSettingsField, FrameSettingsFieldAttribute>>> attributesGroup = new Dictionary<int, IOrderedEnumerable<KeyValuePair<FrameSettingsField, FrameSettingsFieldAttribute>>>();

        /// <summary>Enumerates the keywords corresponding to frame settings properties.</summary>
        internal static readonly string[] frameSettingsKeywords;

        FrameSettings? defaultFrameSettings;
        SerializedFrameSettings serializedFrameSettings;

        static internal FrameSettingsFieldAttribute GetFieldAttribute(FrameSettingsField field) => attributes[field];

        static OverridableFrameSettingsArea()
        {
            attributes = new Dictionary<FrameSettingsField, FrameSettingsFieldAttribute>();
            attributesGroup = new Dictionary<int, IOrderedEnumerable<KeyValuePair<FrameSettingsField, FrameSettingsFieldAttribute>>>();
            Dictionary<FrameSettingsField, string> frameSettingsEnumNameMap = FrameSettingsFieldAttribute.GetEnumNameMap();
            Type type = typeof(FrameSettingsField);
            foreach (FrameSettingsField enumVal in frameSettingsEnumNameMap.Keys)
            {
                attributes[enumVal] = type.GetField(frameSettingsEnumNameMap[enumVal]).GetCustomAttribute<FrameSettingsFieldAttribute>();
            }

            frameSettingsKeywords = attributes
                .Values.Where(v => !string.IsNullOrEmpty(v?.displayedName))
                .Select(v => v.displayedName?.ToLowerInvariant()).ToArray();
        }

        private struct Field
        {
            public FrameSettingsField field;
            public Func<bool> overrideable;
            public bool ignoreDependencies;
            public Func<object> customGetter;
            public Action<object> customSetter;
            public object overridedDefaultValue;
            public bool hideFromUI;
            /// <summary>
            /// Use this field to force displaying mixed values in the UI.
            ///
            /// By default the drawer will displayed mixed values if a bit has different values, but some frame settings
            /// relies on other data, like material quality level. In that case, the other data may have mixed values
            /// and we draw the UI accordingly.
            /// </summary>
            public bool hasMixedValues;

            private GUIContent m_Label;

            public GUIContent label
            {
                get
                {
                    m_Label ??= EditorGUIUtility.TrTextContent(attributes[field].displayedName, attributes[field].tooltip);
                    return m_Label;
                }
            }

            public bool IsOverrideableWithDependencies(SerializedFrameSettings serialized, FrameSettings? defaultFrameSettings)
            {
                FrameSettingsFieldAttribute attribute = attributes[field];
                bool locallyOverrideable = overrideable == null || overrideable();
                FrameSettingsField[] dependencies = attribute.dependencies;
                if (dependencies == null || ignoreDependencies || !locallyOverrideable)
                    return locallyOverrideable;

                if (!defaultFrameSettings.HasValue)
                    return true;

                bool dependenciesOverrideable = true;
                for (int index = dependencies.Length - 1; index >= 0 && dependenciesOverrideable; --index)
                {
                    FrameSettingsField depency = dependencies[index];
                    dependenciesOverrideable &= EvaluateBoolWithOverride(depency, this, defaultFrameSettings, serialized, attribute.IsNegativeDependency(depency));
                }
                return dependenciesOverrideable;
            }
        }
        private List<Field> fields;

        public OverridableFrameSettingsArea(int capacity, FrameSettings? defaultFrameSettings, SerializedFrameSettings serializedFrameSettings)
        {
            fields = new List<Field>(capacity);
            this.defaultFrameSettings = defaultFrameSettings;
            this.serializedFrameSettings = serializedFrameSettings;
        }

        public static OverridableFrameSettingsArea GetGroupContent(int groupIndex, FrameSettings? defaultFrameSettings, SerializedFrameSettings serializedFrameSettings)
        {
            if (!attributesGroup.ContainsKey(groupIndex) || attributesGroup[groupIndex] == null)
                attributesGroup[groupIndex] = attributes?.Where(pair => pair.Value?.group == groupIndex)?.OrderBy(pair => pair.Value.orderInGroup);
            if (!attributesGroup.ContainsKey(groupIndex))
                throw new ArgumentException("Unknown groupIndex");

            var area = new OverridableFrameSettingsArea(attributesGroup[groupIndex].Count(), defaultFrameSettings, serializedFrameSettings);
            foreach (var field in attributesGroup[groupIndex])
            {
                area.Add(field.Key);
            }
            return area;
        }

        /// <summary>
        /// Ammend the info on a FrameSettings drawer in the generation process.
        /// </summary>
        /// <param name="field">Targeted FrameSettings.</param>
        /// <param name="overrideable">Override the method used to say if it will be overrideable or not. If not, the left checkbox will not be drawn.</param>
        /// <param name="ignoreDependencies">Ignore the dependencies when checking if this is overrideable. (Normally, only work if dependency is enabled).</param>
        /// <param name="customGetter">Custom method to get the value. Usefull for non boolean FrameSettings.</param>
        /// <param name="customSetter">Custom method to set the value. Usefull for non boolean FrameSettings.</param>
        /// <param name="overridedDefaultValue">Modify the default value displayed when override is disabled.</param>
        /// <param name="labelOverride">Override the given label with this new one.</param>
        /// <param name="hasMixedValues">Override the miltiple different state manually. Usefull when using customGetter and customSetter. This is static on the Editor run. But Editor is reconstructed if selection change so it should be ok.</param>
        /// <param name="hideInUI">/!\ WARNING: Use with caution. Should not be used with current UX flow. Only usage should be really special cases.</param>
        public void AmmendInfo(FrameSettingsField field, Func<bool> overrideable = null, bool ignoreDependencies = false, Func<object> customGetter = null, Action<object> customSetter = null, object overridedDefaultValue = null, string labelOverride = null, bool hasMixedValues = false, bool hideInUI = false)
        {
            var matchIndex = fields.FindIndex(f => f.field == field);

            if (matchIndex == -1)
                throw new FrameSettingsNotFoundInGroupException("This FrameSettings' group do not contain this field. Be sure that the group parameter of the FrameSettingsFieldAttribute match this OverridableFrameSettingsArea groupIndex.");

            var match = fields[matchIndex];
            if (overrideable != null)
                match.overrideable = overrideable;
            match.ignoreDependencies = ignoreDependencies;
            if (customGetter != null)
                match.customGetter = customGetter;
            if (customSetter != null)
                match.customSetter = customSetter;
            if (overridedDefaultValue != null)
                match.overridedDefaultValue = overridedDefaultValue;
            if (labelOverride != null)
                match.label.text = labelOverride;
            match.hasMixedValues = hasMixedValues;
            match.hideFromUI = hideInUI;
            fields[matchIndex] = match;
        }

        static bool EvaluateBoolWithOverride(FrameSettingsField field, Field forField, FrameSettings? defaultFrameSettings, SerializedFrameSettings serializedFrameSettings, bool negative)
        {
            bool value = false;
            if (serializedFrameSettings.GetOverrides(field))
                value = serializedFrameSettings.IsEnabled(field) ?? false;
            else if (defaultFrameSettings.HasValue)
                value = defaultFrameSettings.Value.IsEnabled(field);
            return value ^ negative;
        }

        /// <summary>Add an overrideable field to be draw when Draw(bool) will be called.</summary>
        /// <param name="serializedFrameSettings">The overrideable property to draw in inspector</param>
        /// <param name="field">The field drawn</param>
        /// <param name="overrideable">The enabler will be used to check if this field could be overrided. If null or have a return value at true, it will be overrided.</param>
        /// <param name="overridedDefaultValue">The value to display when the property is not overrided. If null, use the actual value of it.</param>
        /// <param name="indent">Add this value number of indent when drawing this field.</param>
        void Add(FrameSettingsField field, Func<bool> overrideable = null, Func<object> customGetter = null, Action<object> customSetter = null, object overridedDefaultValue = null)
            => fields.Add(new Field { field = field, overrideable = overrideable, overridedDefaultValue = overridedDefaultValue, customGetter = customGetter, customSetter = customSetter });

        public void Draw(bool withOverride)
        {
            if (fields == null)
                throw new ArgumentOutOfRangeException("Cannot be used without using the constructor with a capacity initializer.");
            if (withOverride & GUI.enabled)
                OverridesHeaders();
            for (int i = 0; i < fields.Count; ++i)
            {
                if (!fields[i].hideFromUI)
                    DrawField(fields[i], withOverride);
            }
        }

        public float Draw(ref Rect rect)
        {
            float rectY = rect.y;

            rect.y += EditorGUIUtility.standardVerticalSpacing;

            if (fields == null)
                throw new ArgumentOutOfRangeException("Cannot be used without using the constructor with a capacity initializer.");

            for (int i = 0; i < fields.Count; ++i)
            {
                if (!fields[i].hideFromUI)
                {
                    DrawField(rect, fields[i]);
                    rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing*2;
                }
            }

            return (rect.position.y + rect.height) - rectY + EditorGUIUtility.standardVerticalSpacing;
        }

        void DrawField(Field field, bool withOverride = false)
        {
            DrawField(GUILayoutUtility.GetRect(1, EditorGUIUtility.singleLineHeight), field, withOverride);
        }

        void DrawField(Rect lineRect, Field field, bool withOverride = false)
        {
            int indentLevel = attributes[field.field].indentLevel;
            if (indentLevel == 0)
                --EditorGUI.indentLevel;    //alignment provided by the space for override checkbox
            else
            {
                for (int i = indentLevel - 1; i > 0; --i)
                    ++EditorGUI.indentLevel;
            }
            bool enabled = field.IsOverrideableWithDependencies(serializedFrameSettings, defaultFrameSettings);
            withOverride &= enabled & GUI.enabled;
            bool shouldBeDisabled = withOverride || !enabled || !GUI.enabled;

            const int k_IndentPerLevel = 15;
            const int k_CheckBoxWidth = 15;
            const int k_CheckboxLabelSeparator = 5;
            const int k_LabelFieldSeparator = 2;
            float indentValue = k_IndentPerLevel * EditorGUI.indentLevel;

            Rect overrideRect = lineRect;
            overrideRect.width = k_CheckBoxWidth;
            Rect labelRect = lineRect;
            labelRect.x += k_CheckBoxWidth + k_CheckboxLabelSeparator;
            labelRect.width = EditorGUIUtility.labelWidth - indentValue;
            Rect fieldRect = lineRect;
            fieldRect.x = labelRect.xMax + k_LabelFieldSeparator;
            fieldRect.width -= fieldRect.x - lineRect.x;

            if (withOverride)
            {
                int currentIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;

                bool mixedValue = serializedFrameSettings.HaveMultipleOverride(field.field);
                bool originalValue = serializedFrameSettings.GetOverrides(field.field) && !mixedValue;
                overrideRect.yMin += 4f;

                // MixedValueState is handled by style for small tickbox for strange reason
                //EditorGUI.showMixedValue = mixedValue;
                bool modifiedValue = EditorGUI.Toggle(overrideRect, overrideTooltip, originalValue, mixedValue ? CoreEditorStyles.smallMixedTickbox : CoreEditorStyles.smallTickbox);
                //EditorGUI.showMixedValue = false;

                if (originalValue ^ modifiedValue)
                    serializedFrameSettings.SetOverrides(field.field, modifiedValue);

                shouldBeDisabled = !modifiedValue;
                EditorGUI.indentLevel = currentIndent;
            }

            using (new SerializedFrameSettings.TitleDrawingScope(labelRect, field.label, serializedFrameSettings))
            {
                HDEditorUtils.HandlePrefixLabelWithIndent(lineRect, labelRect, field.label);
            }

            using (new EditorGUI.DisabledScope(shouldBeDisabled))
            {
                EditorGUI.showMixedValue = serializedFrameSettings.HaveMultipleValue(field.field) || field.hasMixedValues;
                using (new EditorGUILayout.VerticalScope())
                {
                    //the following block will display a default value if provided instead of actual value (case if(true))
                    if (shouldBeDisabled)
                    {
                        if (field.overridedDefaultValue == null)
                        {
                            switch (attributes[field.field].type)
                            {
                                case FrameSettingsFieldAttribute.DisplayType.BoolAsCheckbox:
                                    DrawFieldShape(fieldRect, defaultFrameSettings.HasValue ? defaultFrameSettings.Value.IsEnabled(field.field) : false);
                                    break;
                                case FrameSettingsFieldAttribute.DisplayType.BoolAsEnumPopup:
                                    //shame but it is not possible to use Convert.ChangeType to convert int into enum in current C#
                                    //rely on string parsing for the moment
                                    var oldEnumValue = Enum.Parse(attributes[field.field].targetType, (defaultFrameSettings.HasValue && defaultFrameSettings.Value.IsEnabled(field.field)) ? "1" : "0");
                                    DrawFieldShape(fieldRect, oldEnumValue);
                                    break;
                                case FrameSettingsFieldAttribute.DisplayType.Others:
                                    var oldValue = field.customGetter();
                                    DrawFieldShape(fieldRect, oldValue);
                                    break;
                                default:
                                    throw new ArgumentException("Unknown FrameSettingsFieldAttribute");
                            }
                        }
                        else
                            DrawFieldShape(fieldRect, field.overridedDefaultValue);
                    }
                    else //is enabled
                    {
                        switch (attributes[field.field].type)
                        {
                            case FrameSettingsFieldAttribute.DisplayType.BoolAsCheckbox:
                                bool oldBool = serializedFrameSettings.IsEnabled(field.field) ?? false;
                                bool newBool = (bool)DrawFieldShape(fieldRect, oldBool);
                                if (oldBool ^ newBool)
                                {
                                    if (field.field == FrameSettingsField.Decals || field.field == FrameSettingsField.DecalLayers)
                                        HDRenderPipelineGlobalSettingsPanelProvider.needRefreshVfxErrors = true;
                                    Undo.RecordObject(serializedFrameSettings.serializedObject.targetObject, "Changed FrameSettings " + field.field);
                                    serializedFrameSettings.SetEnabled(field.field, newBool);
                                }
                                break;
                            case FrameSettingsFieldAttribute.DisplayType.BoolAsEnumPopup:
                                //shame but it is not possible to use Convert.ChangeType to convert int into enum in current C#
                                //Also, Enum.Equals and Enum operator!= always send true here. As it seams to compare object reference instead of value.
                                var oldBoolValue = serializedFrameSettings.IsEnabled(field.field);
                                int oldEnumIntValue = -1;
                                int newEnumIntValue;
                                object newEnumValue;
                                if (oldBoolValue.HasValue)
                                {
                                    var oldEnumValue = Enum.GetValues(attributes[field.field].targetType).GetValue(oldBoolValue.Value ? 1 : 0);
                                    newEnumValue = Convert.ChangeType(DrawFieldShape(fieldRect, oldEnumValue), attributes[field.field].targetType);
                                    oldEnumIntValue = ((IConvertible)oldEnumValue).ToInt32(System.Globalization.CultureInfo.CurrentCulture);
                                    newEnumIntValue = ((IConvertible)newEnumValue).ToInt32(System.Globalization.CultureInfo.CurrentCulture);
                                }
                                else //in multi edition, do not assume any previous value
                                {
                                    newEnumIntValue = EditorGUI.Popup(fieldRect, -1, Enum.GetNames(attributes[field.field].targetType));
                                    newEnumValue = newEnumIntValue < 0 ? null : Enum.GetValues(attributes[field.field].targetType).GetValue(newEnumIntValue);
                                }
                                if (oldEnumIntValue != newEnumIntValue)
                                {
                                    Undo.RecordObject(serializedFrameSettings.serializedObject.targetObject, "Changed FrameSettings " + field.field);
                                    serializedFrameSettings.SetEnabled(field.field, Convert.ToInt32(newEnumValue) == 1);
                                }
                                break;
                            case FrameSettingsFieldAttribute.DisplayType.Others:
                                // TODO: refactor to get a default customGetter from GetGroupContent
                                var oldValue = field.customGetter();
                                EditorGUI.BeginChangeCheck();
                                var newValue = DrawFieldShape(fieldRect, oldValue);
                                // We need an extensive check here, otherwise in some case with boxing or polymorphism
                                // the != operator won't be accurate. (This is the case for enum types).
                                var valuesAreEquals = oldValue == null && newValue == null || oldValue != null && oldValue.Equals(newValue);
                                // If the UI reported a change, we also assign values.
                                // When assigning to a multiple selection, the equals check may fail while there was indeed a change.
                                if (EditorGUI.EndChangeCheck() || !valuesAreEquals)
                                {
                                    Undo.RecordObject(serializedFrameSettings.serializedObject.targetObject, "Changed FrameSettings " + field.field);
                                    field.customSetter(newValue);
                                }
                                break;
                            default:
                                throw new ArgumentException("Unknown FrameSettingsFieldAttribute");
                        }
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            if (indentLevel == 0)
            {
                ++EditorGUI.indentLevel;
            }
            else
            {
                for (int i = indentLevel - 1; i > 0; --i)
                {
                    --EditorGUI.indentLevel;
                }
            }
        }

        object DrawFieldShape(Rect rect, object field)
        {
            switch (field)
            {
                case GUIContent content:
                    EditorGUI.LabelField(rect, content);
                    return null;
                case string text:
                    return EditorGUI.TextField(rect, text);
                case bool boolean:
                    return EditorGUI.Toggle(rect, boolean);
                case int integer:
                    return EditorGUI.IntField(rect, integer);
                case float floatValue:
                    return EditorGUI.FloatField(rect, floatValue);
                case Color color:
                    return EditorGUI.ColorField(rect, color);
                case Enum enumeration:
                    return EditorGUI.EnumPopup(rect, enumeration);
                case LayerMask layerMask:
                    return EditorGUI.MaskField(rect, layerMask, GraphicsSettings.currentRenderPipeline.prefixedRenderingLayerMaskNames);
                case UnityEngine.Object unityObject:
                    return EditorGUI.ObjectField(rect, unityObject, field.GetType(), true);
                case SerializedProperty serializedProperty:
                    return EditorGUI.PropertyField(rect, serializedProperty, includeChildren: true);
                default:
                    EditorGUI.LabelField(rect, new GUIContent("Unsupported type"));
                    Debug.LogError($"Unsupported format {field.GetType()} in OverridableSettingsArea.cs. Please add it!");
                    return null;
            }
        }

        void OverridesHeaders()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayoutUtility.GetRect(0f, 17f, GUILayout.ExpandWidth(false));
                if (GUILayout.Button(EditorGUIUtility.TrTextContent("All", "Toggle all overrides on. To maximize performances you should only toggle overrides that you actually need."), CoreEditorStyles.miniLabelButton, GUILayout.Width(17f), GUILayout.ExpandWidth(false)))
                {
                    foreach (var field in fields)
                    {
                        if (field.IsOverrideableWithDependencies(serializedFrameSettings, defaultFrameSettings))
                            serializedFrameSettings.SetOverrides(field.field, true);
                    }
                }

                if (GUILayout.Button(EditorGUIUtility.TrTextContent("None", "Toggle all overrides off."), CoreEditorStyles.miniLabelButton, GUILayout.Width(32f), GUILayout.ExpandWidth(false)))
                {
                    foreach (var field in fields)
                    {
                        if (field.IsOverrideableWithDependencies(serializedFrameSettings, defaultFrameSettings))
                            serializedFrameSettings.SetOverrides(field.field, false);
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }
    }

    class FrameSettingsNotFoundInGroupException : Exception
    {
        public FrameSettingsNotFoundInGroupException(string message)
            : base(message)
        { }
    }
}
