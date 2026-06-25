using UnityEngine;

public class InstructionModalController : MonoBehaviour
{
    [SerializeField] private GameObject instructionModal;

    public void ShowInstruction()
    {
        instructionModal.SetActive(true);
    }

    public void HideInstruction()
    {
        instructionModal.SetActive(false);
    }
}