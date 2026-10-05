using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class DropOnlySocketInteractor : XRSocketInteractor
{
    public override bool CanSelect(IXRSelectInteractable interactable) // Another subclass...
    {
        if (!base.CanSelect(interactable))
        {
            return false;
        }

        foreach(var selector in interactable.interactorsSelecting)
        {
            if(selector != this) 
            {
                return false;
            }
        }

   //     // Don't take object from other one
  //      if (interactable.isSelected)
   //     {
   //         return false;
   //     }

        return true;
    }
}