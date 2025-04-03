using UnityEditor;
using UnityEditor.Scripting.Python;

public class MenuItem_NewPythonScript_Class
{
   [MenuItem("Python Scripts/New Python Script")]
   public static void NewPythonScript()
   {
       PythonRunner.RunFile("Assets/Script_py/new_python_script.py");
       }
};
