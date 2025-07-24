using UnityEngine;
using UnityEngine.EventSystems; // Belangrijk voor IPointerUpHandler
using UnityEngine.Events;       // Belangrijk voor UnityEvent

public class ButtonReleaseEvent : MonoBehaviour, IPointerUpHandler
{
    // Dit is je eigen OnRelease UnityEvent die je in de Inspector kunt instellen
    // of waaraan je via code een listener kunt toevoegen.
    public UnityEvent OnButtonRelease = new UnityEvent();

    // Deze methode wordt automatisch aangeroepen door het Unity Event System
    // wanneer de muisknop/vinger wordt losgelaten boven dit GameObject.
    public void OnPointerUp(PointerEventData eventData)
    {
        // Debug.Log("ButtonReleaseEvent: Knop losgelaten!");

        // Roep alle geregistreerde listeners aan
        OnButtonRelease.Invoke();
    }

    // Optioneel: Voorbeeld van hoe je deze event in een andere script zou gebruiken
    private void Start()
    {
        // Voeg een listener toe via code
        // OnButtonRelease.AddListener(MyCustomReleaseMethod);
    }

 
}