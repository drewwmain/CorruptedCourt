using UnityEngine;

public class UIIconSpawner : MonoBehaviour
{
    private GameObject current3DIcon;

    // Call this from your UIManager when updating the inventory
    public void SetIcon(GameObject prefabToSpawn)
    {
        // 1. Destroy any existing 3D icon in this slot
        if (current3DIcon != null)
        {
            Destroy(current3DIcon);
        }

        // 2. If the slot is empty, stop here
        if (prefabToSpawn == null) return;

        // 3. Spawn the new 3D Synty icon inside this UI element
        current3DIcon = Instantiate(prefabToSpawn, transform);

        // 4. Reset position and rotation to center it in the UI slot
        current3DIcon.transform.localPosition = Vector3.zero;
        current3DIcon.transform.localRotation = Quaternion.identity; // Adjust this if the Synty model faces the wrong way

        // 5. Scale it up! UI space is massive compared to world space. 
        // You may need to tweak this multiplier (e.g., 50, 100, 150) depending on the Canvas scaler.
        current3DIcon.transform.localScale = new Vector3(100f, 100f, 100f);

        // 6. Force the object and all its child meshes to the UI layer so the camera renders it correctly
        SetLayerRecursively(current3DIcon, LayerMask.NameToLayer("UI"));
    }

    // Helper method to ensure the 3D model and its materials render on the UI layer
    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}