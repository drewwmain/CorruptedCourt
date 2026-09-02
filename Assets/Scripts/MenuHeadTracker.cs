using UnityEngine;

public class MenuHeadTracker : MonoBehaviour
{
    [Header("Bone References")]
    [Tooltip("Drag the character's Head bone here")]
    public Transform headBone;

    [Header("Tracking Settings")]
    public float maxHorizontalAngle = 45f; 
    public float maxVerticalAngle = 25f;   
    public float lookSpeed = 5f;           
    
    [Tooltip("Offsets the baseline up/down tilt. Tweak this until the eyes meet the cursor!")]
    public float verticalOffset = 15f; 

    [Tooltip("Where is the character on the screen? 0.5 is center, 0.75 is the right side.")]
    public float horizontalCenter = 0.75f; // <--- NEW VARIABLE
    private Quaternion currentRotationOffset = Quaternion.identity;

    void LateUpdate()
    {
        if (headBone == null) return;

        // 1. Get the mouse position as a percentage of the screen (0.0 to 1.0)
        float mousePercentX = Mathf.Clamp01(Input.mousePosition.x / Screen.width);
        float mousePercentY = Mathf.Clamp01(Input.mousePosition.y / Screen.height);

        // 2. Calculate the raw angles using our customizable centers
        // Using horizontalCenter instead of 0.5f shifts the "forward" focus to the right side of the screen
        float rawYaw = -(mousePercentX - horizontalCenter) * 2f * maxHorizontalAngle;
        float rawPitch = (-(mousePercentY - 0.5f) * 2f * maxVerticalAngle) + verticalOffset; 

        // 3. Clamp the angles so the head doesn't rotate too far when the mouse is far away
        float targetYaw = Mathf.Clamp(rawYaw, -maxHorizontalAngle, maxHorizontalAngle);
        float targetPitch = Mathf.Clamp(rawPitch, -maxVerticalAngle, maxVerticalAngle);

        // 4. Create the rotation offset
        Quaternion targetRotation = Quaternion.Euler(targetPitch, targetYaw, 0f);

        // 5. Smoothly blend the current offset towards the target offset
        currentRotationOffset = Quaternion.Slerp(currentRotationOffset, targetRotation, Time.deltaTime * lookSpeed);

        // 6. Apply the offset ON TOP of the idle animation!
        headBone.localRotation = headBone.localRotation * currentRotationOffset;
    }
}