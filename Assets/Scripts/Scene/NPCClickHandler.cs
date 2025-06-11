using UnityEngine;

public class NPCClickHandler : MonoBehaviour
{
    public bool isRecording;

    private void OnMouseDown()
    {
        Debug.Log("NPC Click Handler: <<Click>>");
        isRecording = true;
    }

    private void OnMouseUp()
    {
        Debug.Log("NPC Click Handler: <<Release>>");
        isRecording = false;
    }
}
