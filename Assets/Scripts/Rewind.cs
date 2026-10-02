using System.Collections.Generic;
using UnityEngine;



//ce script est à mettre sur tous les Gameobjects qui doivent pouvoir remonter le temps
public class Rewind : MonoBehaviour
{
    [System.Serializable]
    private struct TransformState
    {
        public Vector3 position;
        public Quaternion rotation;

        public TransformState(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }

    [Header("Recording")]
    [SerializeField] private float recordInterval = 0.05f;
    [SerializeField] private float maxRecordingTime = 10f;

    private List<TransformState> history = new List<TransformState>();

    private bool isRecording = false;
    private float recordTimer = 0f;

    private void Update()
    {
        if (!isRecording)
            return;

        recordTimer += Time.deltaTime;

        if (recordTimer >= recordInterval)
        {
            recordTimer = 0f;
            SaveState();
        }
    }

    public void StartRecording()
    {
        history.Clear();

        isRecording = true;
        recordTimer = 0f;

        // Enregistre immédiatement l'état initial
        SaveState();
    }

    public void RoleBack()
    {
        if (history.Count == 0)
            return;

        // Récupère le dernier état enregistré
        TransformState state = history[history.Count - 1];

        transform.position = state.position;
        transform.rotation = state.rotation;

        history.RemoveAt(history.Count - 1);
    }

    private void SaveState()
    {
        history.Add(new TransformState(
            transform.position,
            transform.rotation
        ));

        // Limite la taille de l'historique
        int maxStates = Mathf.CeilToInt(maxRecordingTime / recordInterval);

        if (history.Count > maxStates)
        {
            history.RemoveAt(0);
        }
    }
}