using UnityEditor;
using UnityEditorInternal;
using UnityEngine;


[CustomEditor(typeof(RigExecutor))]
public class RigExecutor_EDITOR : Editor
{
    private RigExecutor _rigExecutor;
    public SerializedProperty activeRig;
    private ReorderableList _list;
    // Start is called before the first frame update
    private void OnEnable()
    {
        _rigExecutor = (RigExecutor)target;

        activeRig = serializedObject.FindProperty("rigActive");

        _list = new ReorderableList(serializedObject, serializedObject.FindProperty("rigListComponents"), true, true, true, true);
        _list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            var element = _list.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += 2;
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width - 30, EditorGUIUtility.singleLineHeight),
                element.FindPropertyRelative("rigList"), GUIContent.none);
            EditorGUI.PropertyField(
                new Rect(rect.x + (rect.width - 30), rect.y, rect.width - 90, EditorGUIUtility.singleLineHeight),
                element.FindPropertyRelative("active"), GUIContent.none);
        };

    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(activeRig);
        EditorGUILayout.Separator();
        _list.DoLayoutList();

        if (GUILayout.Button("Rebuild")) { _rigExecutor.RigsInitialized(); }

        serializedObject.ApplyModifiedProperties();
    }
}
