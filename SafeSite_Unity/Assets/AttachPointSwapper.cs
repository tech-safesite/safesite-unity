using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class AttachPointSwapper : XRGrabInteractable
{

    [SerializeField]
    private Transform rightHandAttach;

    [SerializeField]
    private Transform leftHandAttach;
    [SerializeField]
    private Transform controllerAttach;

    public override Transform GetAttachTransform(IXRInteractor interactor) // Maybe move to OnSelectEntering? Or seperate script that doesn't replace Grabbable?
    {
        if(interactor == null)
        {
            return rightHandAttach;
        }

        if( interactor is XRSocketInteractor) // Fixes issue with Socket not recognising because of below. Another case for handling attach swapping externally....
        {
            return rightHandAttach;
        }

        string name = interactor.transform.parent.name.ToLower();

        print("Name Found Grabbing : " + name);
        if (name.Contains("hand"))
        {
            if (name.Contains("left"))
            {
                attachTransform = leftHandAttach;
                return attachTransform;
            }

            else
            {
                attachTransform = rightHandAttach;
                return attachTransform;
            }


        }


        if (name.Contains("controller"))
        {
            print("Picking Up WIth Controller. Set Attach To Controller ");
            attachTransform = controllerAttach;
            return attachTransform;

        }

        return base.GetAttachTransform(interactor);
    }

}
