using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CustomFrustumLocalSpace : MonoBehaviour
{
    public Camera finder;
    public float xRatio = 16;
    public float yRatio = 9;
    public float customOffset = 0.1f;
    public Transform capturePoint;
    public PlayerController controller;

    [HideInInspector]
    public Polaroid polaroid;

    float aspectRatio = 1;
    GameObject leftPrimitivePlane, rightPrimitivePlane, topPrimitivePlane, bottomPrimitivePlane, frustumObject;
    MeshFilter leftPrimitivePlaneMF, rightPrimitivePlaneMF, topPrimitivePlaneMF, bottomPrimitivePlaneMF, frustumObjectMF;
    MeshCollider leftPrimitivePlaneMC, rightPrimitivePlaneMC, topPrimitivePlaneMC, bottomPrimitivePlaneMC, frustumObjectMC;
    List<GameObject> leftToCut, rightToCut, topToCut, bottomToCut, objectsInFrustum;
    Vector3 leftUpFrustum, rightUpFrustum, leftDownFrustum, rightDownFrustum, cameraPos;
    Plane leftPlane, rightPlane, topPlane, bottomPlane;
    PolaroidFilm activeFilm;
    Vector3 forwardVector;
    bool isTakingPicture;
    GameObject ending;
}

    void Start()
    {
        Debug.Log("[CustomFrustum] Initializing planes and colliders");

        leftPrimitivePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        leftPrimitivePlane.name = "LeftCameraPlane";
        rightPrimitivePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        rightPrimitivePlane.name = "RightCameraPlane";
        topPrimitivePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        topPrimitivePlane.name = "TopCameraPlane";
        bottomPrimitivePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        bottomPrimitivePlane.name = "BottomCameraPlane";
        frustumObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        frustumObject.name = "FrustumObject";

        // --- Setup Colliders ---
        leftPrimitivePlaneMC = leftPrimitivePlane.GetComponent<MeshCollider>();
        leftPrimitivePlaneMC.convex = true;
        leftPrimitivePlaneMC.isTrigger = true;
        leftPrimitivePlaneMC.enabled = false;

        rightPrimitivePlaneMC = rightPrimitivePlane.GetComponent<MeshCollider>();
        rightPrimitivePlaneMC.convex = true;
        rightPrimitivePlaneMC.isTrigger = true;
        rightPrimitivePlaneMC.enabled = false;

        topPrimitivePlaneMC = topPrimitivePlane.GetComponent<MeshCollider>();
        topPrimitivePlaneMC.convex = true;
        topPrimitivePlaneMC.isTrigger = true;
        topPrimitivePlaneMC.enabled = false;

        bottomPrimitivePlaneMC = bottomPrimitivePlane.GetComponent<MeshCollider>();
        bottomPrimitivePlaneMC.convex = true;
        bottomPrimitivePlaneMC.isTrigger = true;
        bottomPrimitivePlaneMC.enabled = false;

        frustumObjectMC = frustumObject.GetComponent<MeshCollider>();
        frustumObjectMC.convex = true;
        frustumObjectMC.isTrigger = true;
        frustumObjectMC.enabled = false;

        // --- Setup MeshFilters ---
        leftPrimitivePlaneMF = leftPrimitivePlane.GetComponent<MeshFilter>();
        rightPrimitivePlaneMF = rightPrimitivePlane.GetComponent<MeshFilter>();
        topPrimitivePlaneMF = topPrimitivePlane.GetComponent<MeshFilter>();
        bottomPrimitivePlaneMF = bottomPrimitivePlane.GetComponent<MeshFilter>();
        frustumObjectMF = frustumObject.GetComponent<MeshFilter>();

        // --- Hide Renderers ---
        leftPrimitivePlane.GetComponent<MeshRenderer>().enabled = false;
        rightPrimitivePlane.GetComponent<MeshRenderer>().enabled = false;
        topPrimitivePlane.GetComponent<MeshRenderer>().enabled = false;
        bottomPrimitivePlane.GetComponent<MeshRenderer>().enabled = false;
        frustumObject.GetComponent<MeshRenderer>().enabled = false;

        // --- Add Collision Checkers ---
        var leftChecker = leftPrimitivePlane.AddComponent<CollisionChecker>();
        leftChecker.frustumLocalSpace = this;
        leftChecker.side = 0;

        var rightChecker = rightPrimitivePlane.AddComponent<CollisionChecker>();
        rightChecker.frustumLocalSpace = this;
        rightChecker.side = 1;

        var topChecker = topPrimitivePlane.AddComponent<CollisionChecker>();
        topChecker.frustumLocalSpace = this;
        topChecker.side = 2;

        var bottomChecker = bottomPrimitivePlane.AddComponent<CollisionChecker>();
        bottomChecker.frustumLocalSpace = this;
        bottomChecker.side = 3;

        var frustumChecker = frustumObject.AddComponent<CollisionChecker>();
        frustumChecker.frustumLocalSpace = this;
        frustumChecker.side = 4;
    }

    public void StartCutOperation(bool takingPicture)
    {
        StartCoroutine(DelayedCut(takingPicture));
    }

    public IEnumerator DelayedCut(bool takingPicture)
    {
        Debug.Log("[CustomFrustum] Waiting for colliders to register overlaps...");
        yield return new WaitForFixedUpdate(); // wait for physics to process
        yield return null; // wait one more frame, just to be safe

        Debug.Log("[CustomFrustum] Now performing Cut()");
        Cut(takingPicture);
    }

    public void Cut(bool isTakingPic)
    {
        Debug.Log($"[CustomFrustum] Cut() called | TakingPicture={isTakingPic} | Time={Time.time}");

        isTakingPicture = isTakingPic;
        Debug.Log($"[CustomFrustum] Starting Cut() | TakingPicture = {isTakingPic}");

        controller.ChangePlayerState(false);
        aspectRatio = finder.aspect;
        var frustumHeight = 2.0f * finder.farClipPlane * Mathf.Tan(finder.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var frustumWidth = frustumHeight * aspectRatio;

        Debug.Log($"[CustomFrustum] Aspect={aspectRatio:F2}, FOV={finder.fieldOfView}, Width={frustumWidth:F2}, Height={frustumHeight:F2}");

        // --- 1. Calculate Frustum Corners in World Space ---
        leftUpFrustum = new Vector3(-frustumWidth / 2, frustumHeight / 2, finder.farClipPlane);
        rightUpFrustum = new Vector3(frustumWidth / 2, frustumHeight / 2, finder.farClipPlane);
        leftDownFrustum = new Vector3(-frustumWidth / 2, -frustumHeight / 2, finder.farClipPlane);
        rightDownFrustum = new Vector3(frustumWidth / 2, -frustumHeight / 2, finder.farClipPlane);

        leftUpFrustum = capturePoint.transform.TransformPoint(leftUpFrustum);
        rightUpFrustum = capturePoint.transform.TransformPoint(rightUpFrustum);
        leftDownFrustum = capturePoint.transform.TransformPoint(leftDownFrustum);
        rightDownFrustum = capturePoint.transform.TransformPoint(rightDownFrustum);

        cameraPos = capturePoint.transform.position;
        forwardVector = capturePoint.transform.forward;

        Debug.Log($"[CustomFrustum] CameraPos={cameraPos}, Forward={forwardVector}");

        // --- 2. Calculate Mathematical Planes ---
        leftPlane = new Plane(cameraPos, leftUpFrustum, leftDownFrustum);
        rightPlane = new Plane(cameraPos, rightDownFrustum, rightUpFrustum);
        topPlane = new Plane(cameraPos, rightUpFrustum, leftUpFrustum);
        bottomPlane = new Plane(cameraPos, leftDownFrustum, rightDownFrustum);

        // --- 3. Update Collider Meshes AND Transforms ---
        // This is the CRITICAL fix. We move the colliders to the camera
        // and update their meshes to match.
        var leftOffset = leftPlane.normal * customOffset;
        leftPrimitivePlane.transform.SetPositionAndRotation(capturePoint.position, capturePoint.rotation);
        leftPrimitivePlaneMF.mesh = CreateBoxMesh(cameraPos, leftUpFrustum, (leftUpFrustum + leftDownFrustum) / 2, leftDownFrustum,
        leftDownFrustum + leftOffset, ((leftUpFrustum + leftDownFrustum) / 2) + leftOffset, leftUpFrustum + leftOffset, cameraPos + leftOffset);
        leftPrimitivePlaneMC.sharedMesh = leftPrimitivePlaneMF.mesh;

        var rightOffset = rightPlane.normal * customOffset;
        rightPrimitivePlane.transform.SetPositionAndRotation(capturePoint.position, capturePoint.rotation);
        rightPrimitivePlaneMF.mesh = CreateBoxMesh(cameraPos, rightDownFrustum, (rightUpFrustum + rightDownFrustum) / 2, rightUpFrustum,
        rightUpFrustum + rightOffset, ((rightUpFrustum + rightDownFrustum) / 2) + rightOffset, rightDownFrustum + rightOffset, cameraPos + rightOffset);
        rightPrimitivePlaneMC.sharedMesh = rightPrimitivePlaneMF.mesh;

        var topOffset = topPlane.normal * customOffset;
        topPrimitivePlane.transform.SetPositionAndRotation(capturePoint.position, capturePoint.rotation);
        topPrimitivePlaneMF.mesh = CreateBoxMesh(cameraPos, rightUpFrustum, (leftUpFrustum + rightUpFrustum) / 2, leftUpFrustum,
        leftUpFrustum + topOffset, ((leftUpFrustum + rightUpFrustum) / 2) + topOffset, rightUpFrustum + topOffset, cameraPos + topOffset);
        topPrimitivePlaneMC.sharedMesh = topPrimitivePlaneMF.mesh;

        var bottomOffset = bottomPlane.normal * customOffset;
        bottomPrimitivePlane.transform.SetPositionAndRotation(capturePoint.position, capturePoint.rotation);
        bottomPrimitivePlaneMF.mesh = CreateBoxMesh(cameraPos, leftDownFrustum, (leftDownFrustum + rightDownFrustum) / 2, rightDownFrustum,
        rightDownFrustum + bottomOffset, ((leftDownFrustum + rightDownFrustum) / 2) + bottomOffset, leftDownFrustum + bottomOffset, cameraPos + bottomOffset);
        bottomPrimitivePlaneMC.sharedMesh = bottomPrimitivePlaneMF.mesh;

        // --- 4. Clear lists and enable colliders ---
        leftToCut = new List<GameObject>();
        rightToCut = new List<GameObject>();
        topToCut = new List<GameObject>();
        bottomToCut = new List<GameObject>();
        objectsInFrustum = new List<GameObject>();
        ending = null;

        leftPrimitivePlaneMC.enabled = true;
        rightPrimitivePlaneMC.enabled = true;
        topPrimitivePlaneMC.enabled = true;
        bottomPrimitivePlaneMC.enabled = true;

        StartCoroutine(TestCut(isTakingPicture));
    }

    IEnumerator TestCut(bool isTakingPicture)
    {
        Debug.Log("[CustomFrustum] >>> TestCut coroutine started");

        // --- Wait for physics (FIXED) ---
        Debug.Log("[CustomFrustum] Waiting for side-plane physics overlaps...");
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        leftPrimitivePlaneMC.enabled = false;
        rightPrimitivePlaneMC.enabled = false;
        topPrimitivePlaneMC.enabled = false;
        bottomPrimitivePlaneMC.enabled = false;

        Debug.Log($"[CustomFrustum] Originals Detected: Left={leftToCut.Count}, Right={rightToCut.Count}, Top={topToCut.Count}, Bottom={bottomToCut.Count}");

        List<GameObject> allCutPieces = new List<GameObject>();

        if (isTakingPicture)
        {
            // --- "TAKE PHOTO" (NON-DESTRUCTIVE) ---
            Debug.Log("[CustomFrustum] Mode: Taking Picture (Non-Destructive)");

            // 1. Create a set of all original objects detected
            var allOriginals = new HashSet<GameObject>(leftToCut);
            allOriginals.UnionWith(rightToCut);
            allOriginals.UnionWith(topToCut);
            allOriginals.UnionWith(bottomToCut);

            // 2. Create temporary clones of all originals
            var originalToCloneMap = new Dictionary<GameObject, GameObject>();
            var allClones = new List<GameObject>();

            foreach (var original in allOriginals)
            {
                if (original == null) continue;
                var clone = Instantiate(original, original.transform.position, original.transform.rotation);
                clone.transform.localScale = original.transform.localScale;
                clone.AddComponent<CutPiece>();
                originalToCloneMap[original] = clone;
                allClones.Add(clone);
            }

            // 3. Create new lists pointing to the CLONES
            var leftClones = leftToCut.Where(o => o != null && originalToCloneMap.ContainsKey(o)).Select(o => originalToCloneMap[o]).ToList();
            var rightClones = rightToCut.Where(o => o != null && originalToCloneMap.ContainsKey(o)).Select(o => originalToCloneMap[o]).ToList();
            var topClones = topToCut.Where(o => o != null && originalToCloneMap.ContainsKey(o)).Select(o => originalToCloneMap[o]).ToList();
            var bottomClones = bottomToCut.Where(o => o != null && originalToCloneMap.ContainsKey(o)).Select(o => originalToCloneMap[o]).ToList();

            // 4. Perform all slicing operations on the CLONES using CutPiece logic

            // --- LEFT CUT (on clones) ---
            foreach (var obj in leftClones)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>();
                if (cutPiece.chunks == null || cutPiece.chunks.Count == 0) cutPiece.AddChunk(obj);

                int initialCount = cutPiece.chunks.Count;
                for (int i = 0; i < initialCount; i++)
                {
                    var chunkToCut = cutPiece.chunks[i];
                    if (chunkToCut == null) continue;
                    var newPiece = Cutter.Cut(chunkToCut, (leftDownFrustum + leftUpFrustum + cameraPos) / 3, leftPlane.normal);
                    if (newPiece != null) cutPiece.AddChunk(newPiece);
                }
            }

            // --- RIGHT CUT (on clones) ---
            foreach (var obj in rightClones)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>();
                if (cutPiece.chunks == null || cutPiece.chunks.Count == 0) cutPiece.AddChunk(obj);

                int initialCount = cutPiece.chunks.Count;
                for (int i = 0; i < initialCount; i++)
                {
                    var chunkToCut = cutPiece.chunks[i];
                    if (chunkToCut == null) continue;
                    var newPiece = Cutter.Cut(chunkToCut, (rightDownFrustum + rightUpFrustum + cameraPos) / 3, rightPlane.normal);
                    if (newPiece != null) cutPiece.AddChunk(newPiece);
                }
            }

            // --- TOP CUT (on clones) ---
            foreach (var obj in topClones)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>();
                if (cutPiece.chunks == null || cutPiece.chunks.Count == 0) cutPiece.AddChunk(obj);

                int initialCount = cutPiece.chunks.Count;
                for (int i = 0; i < initialCount; i++)
                {
                    var chunkToCut = cutPiece.chunks[i];
                    if (chunkToCut == null) continue;
                    var newPiece = Cutter.Cut(chunkToCut, (rightUpFrustum + leftUpFrustum + cameraPos) / 3, topPlane.normal);
                    if (newPiece != null) cutPiece.AddChunk(newPiece);
                }
            }

            // --- BOTTOM CUT (on clones) ---
            foreach (var obj in bottomClones)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>();
                if (cutPiece.chunks == null || cutPiece.chunks.Count == 0) cutPiece.AddChunk(obj);

                int initialCount = cutPiece.chunks.Count;
                for (int i = 0; i < initialCount; i++)
                {
                    var chunkToCut = cutPiece.chunks[i];
                    if (chunkToCut == null) continue;
                    var newPiece = Cutter.Cut(chunkToCut, (rightDownFrustum + leftDownFrustum + cameraPos) / 3, bottomPlane.normal);
                    if (newPiece != null) cutPiece.AddChunk(newPiece);
                }
            }

            // 5. Run the frustum check. The CollisionChecker will now find the CLONE pieces.
            // --- FIX: Update frustum mesh and position BEFORE enabling ---
            frustumObject.transform.SetPositionAndRotation(capturePoint.position, capturePoint.rotation);
            frustumObjectMF.mesh = CreateFrustumObject(cameraPos + (forwardVector * -customOffset), rightDownFrustum + (rightPlane.normal * -customOffset), rightUpFrustum + (rightPlane.normal * -customOffset), leftUpFrustum + (leftPlane.normal * -customOffset), leftDownFrustum + (leftPlane.normal * -customOffset));
            frustumObjectMC.sharedMesh = frustumObjectMF.mesh;
            frustumObjectMC.enabled = true;

            Debug.Log("[CustomFrustum] Waiting for frustum volume physics (on clones)...");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            frustumObjectMC.enabled = false;

            // 'objectsInFrustum' is now filled with the final, sliced CLONE pieces
            Debug.Log($"[CustomFrustum] Sliced clone pieces inside frustum = {objectsInFrustum.Count}");

            if (ending != null) objectsInFrustum.Add(ending); // Add ending object if found

            // 6. Create the film using these clone pieces
            activeFilm = new PolaroidFilm(objectsInFrustum, capturePoint);
            Debug.Log($"[CustomFrustum] Film created with {objectsInFrustum.Count} objects.");

            // 7. Clean up ALL temporary clones and pieces
            Debug.Log($"[CustomFrustum] Cleaning up temporary clones.");
            foreach (var clone in allClones)
            {
                if (clone == null) continue;
                var cutPiece = clone.GetComponent<CutPiece>();
                if (cutPiece != null && cutPiece.chunks != null)
                {
                    foreach (var chunk in cutPiece.chunks)
                    {
                        if (chunk != null) Destroy(chunk);
                    }
                }
                else
                {
                    Destroy(clone);
                }
            }
        }
        else
        {
            // --- "PASTE" (DESTRUCTIVE) ---
            Debug.Log("[CustomFrustum] Mode: Pasting (Destructive)");

            // 1. Perform slicing on ORIGINALS using CutPiece logic

            // --- LEFT CUT (on originals) ---
            foreach (var obj in leftToCut)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>() ?? obj.AddComponent<CutPiece>();
                if (cutPiece.chunks == null || cutPiece.chunks.Count == 0) cutPiece.AddChunk(obj);

                int initialCount = cutPiece.chunks.Count;
                for (int i = 0; i < initialCount; i++)
                {
                    var chunkToCut = cutPiece.chunks[i];
                    if (chunkToCut == null) continue;
                    var newPiece = Cutter.Cut(chunkToCut, (leftDownFrustum + leftUpFrustum + cameraPos) / 3, leftPlane.normal);
                    if (newPiece != null)
                    {
                        cutPiece.AddChunk(newPiece);
                        allCutPieces.Add(newPiece); // Track for cleanup
                    }
                }
            }

            // --- RIGHT CUT (on originals) ---
            foreach (var obj in rightToCut)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>() ?? obj.AddComponent<CutPiece>();
                if (cutPiece.chunks == null || cutPiece.chunks.Count == 0) cutPiece.AddChunk(obj);

                int initialCount = cutPiece.chunks.Count;
                for (int i = 0; i < initialCount; i++)
                {
                    var chunkToCut = cutPiece.chunks[i];
                    if (chunkToCut == null) continue;
                    var newPiece = Cutter.Cut(chunkToCut, (rightDownFrustum + rightUpFrustum + cameraPos) / 3, rightPlane.normal);
                    if (newPiece != null)
                    {
                        cutPiece.AddChunk(newPiece);
                        allCutPieces.Add(newPiece);
                    }
                }
            }

            // --- TOP CUT (on originals) ---
            foreach (var obj in topToCut)
            {
                if (obj == null) continue;
                var cutPiece = obj.GetComponent<CutPiece>() ?? obj.AddComponent