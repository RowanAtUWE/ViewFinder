using System.Collections.Generic;
using UnityEngine;

/*
As mentioned in the video, this is a custom implementation based on Kristin Lague's project. 
If you've downloaded this repo for the cutting part, I suggest starting with this repo: https://github.com/KristinLague/Mesh-Cutting
and this video: https://www.youtube.com/watch?v=1UsuZsaUUng&t=7s 
*/

public class Cutter : MonoBehaviour
{
    // Note: The static 'isBusy' flag has been removed to prevent race conditions.
    private static Mesh originalMesh;

    public static GameObject Cut(GameObject originalGameObject, Vector3 contactPoint, Vector3 cutNormal)
    {
        Plane cutPlane = new Plane(
            originalGameObject.transform.InverseTransformDirection(-cutNormal),
            originalGameObject.transform.InverseTransformPoint(contactPoint)
        );

        originalMesh = originalGameObject.GetComponent<MeshFilter>().mesh;

        if (originalMesh == null)
        {
            Debug.LogError("Need mesh to cut");
            return null; // isBusy removed here too
        }

        // Capture original materials early so we can copy them exactly
        var originalRenderer = originalGameObject.GetComponent<MeshRenderer>();
        Material[] originalMaterials = null;
        if (originalRenderer != null && originalRenderer.sharedMaterials != null && originalRenderer.sharedMaterials.Length > 0)
            originalMaterials = originalRenderer.sharedMaterials;
        else // Fallback if no materials found
            originalMaterials = new Material[] { new Material(Shader.Find("Standard")) };

        List<Vector3> addedVertices = new List<Vector3>();
        GeneratedMesh leftMesh = new GeneratedMesh();
        GeneratedMesh rightMesh = new GeneratedMesh();

        SeparateMeshes(leftMesh, rightMesh, cutPlane, addedVertices);
        FillCut(addedVertices, cutPlane, leftMesh, rightMesh);

        Mesh finishedLeftMesh = leftMesh.GetGeneratedMesh();
        Mesh finishedRightMesh = rightMesh.GetGeneratedMesh();

        Debug.Log($"[Cutter] Cleaning left mesh: verts={finishedLeftMesh.vertexCount}, submeshes={finishedLeftMesh.subMeshCount}");
        Debug.Log($"[Cutter] Cleaning right mesh: verts={finishedRightMesh.vertexCount}, submeshes={finishedRightMesh.subMeshCount}");

        // --- Try to clean meshes to remove degenerate triangles ---
        try { CleanMesh(finishedLeftMesh); }
        catch (System.Exception e) { Debug.LogWarning($"[Cutter] CleanMesh failed for LEFT mesh: {e.Message}"); }

        try { CleanMesh(finishedRightMesh); }
        catch (System.Exception e) { Debug.LogWarning($"[Cutter] CleanMesh failed for RIGHT mesh: {e.Message}"); }

        // Remove existing colliders from original
        foreach (var col in originalGameObject.GetComponents<Collider>())
            Destroy(col);

        // Prepare left mesh and assign to originalGameObject
        PrepareMeshForCollider(finishedLeftMesh);
        originalGameObject.GetComponent<MeshFilter>().mesh = finishedLeftMesh;

        // Build materials array sized to match submesh count while preserving original materials/shaders
        Material[] leftMats = BuildMaterialArrayForMesh(originalMaterials, finishedLeftMesh.subMeshCount);
        ApplyColliderAndMaterials(originalGameObject, finishedLeftMesh, leftMats);

        // Create right GameObject
        GameObject right = new GameObject(originalGameObject.name + "_Right");
        right.transform.SetPositionAndRotation(originalGameObject.transform.position, originalGameObject.transform.rotation);
        right.transform.localScale = originalGameObject.transform.localScale;

        // Add components & assign mesh
        var rightFilter = right.AddComponent<MeshFilter>();
        rightFilter.mesh = finishedRightMesh;
        PrepareMeshForCollider(finishedRightMesh); // Prepare right mesh too

        // Apply materials and collider to the new right object
        var rightRenderer = right.AddComponent<MeshRenderer>();
        Material[] rightMats = BuildMaterialArrayForMesh(originalMaterials, finishedRightMesh.subMeshCount);
        ApplyColliderAndMaterials(right, finishedRightMesh, rightMats);

        // Add Rigidbody (optional, adjust as needed)
        var rightRb = right.AddComponent<Rigidbody>();
        rightRb.isKinematic = true;

        right.layer = LayerMask.NameToLayer("Cuttable");

        // isBusy = false; // Removed this line too
        return right;
    }

    // --- HELPER FUNCTIONS from the merged version ---

    private static Material[] BuildMaterialArrayForMesh(Material[] sourceMaterials, int meshSubMeshCount)
    {
        int targetCount = Mathf.Max(1, meshSubMeshCount); // Ensure at least one material slot
        Material[] mats = new Material[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            // copy source material by index, or wrap around, or use first/default as fallback
            if (sourceMaterials != null && sourceMaterials.Length > 0)
            {
                int sourceIndex = i % sourceMaterials.Length;
                mats[i] = sourceMaterials[sourceIndex] ?? new Material(Shader.Find("Standard")); // Fallback if source is null
            }
            else
            {
                mats[i] = new Material(Shader.Find("Standard")); // Default material if no source
            }
        }
        return mats;
    }

    private static void ApplyColliderAndMaterials(GameObject go, Mesh mesh, Material[] mats)
    {
        if (go == null || mesh == null) return;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();

        // assign materials (use sharedMaterials to preserve shader references)
        renderer.sharedMaterials = mats;

        // --- Safely add MeshCollider ---
        // Destroy existing one first to avoid conflicts
        var existingCollider = go.GetComponent<MeshCollider>();
        if (existingCollider != null)
        {
            existingCollider.sharedMesh = null; // Important before destroying
            Destroy(existingCollider);
        }

        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;

        // Try setting convex, but handle potential QuickHull errors
        try
        {
            if (mesh.vertexCount > 0) // Avoid errors on empty meshes
            {
                col.convex = true;
            }
            else
            {
                Debug.LogWarning($"[Cutter] Mesh on {go.name} is empty, cannot create convex collider.");
                col.convex = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Cutter] Convex collider failed on {go.name}: {e.Message}. Using non-convex instead.");
            col.convex = false;
        }
    }

    private static void PrepareMeshForCollider(Mesh mesh)
    {
        if (mesh == null) return;

        // Enable 32-bit indices if vertex count is high (important for large meshes)
        if (mesh.vertexCount > 65000 && mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
        {
            try { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
            catch { Debug.LogWarning("[Cutter] Failed to set UInt32 index format. Large meshes might cause issues."); }
        }

        mesh.RecalculateNormals();
        // mesh.RecalculateTangents(); // Tangents usually not needed for colliders
        mesh.RecalculateBounds();
    }

    private static void CleanMesh(Mesh mesh)
    {
        if (mesh == null || mesh.vertexCount == 0) return;

        Vector3[] verts = mesh.vertices;
        int originalSubMeshCount = mesh.subMeshCount;
        if (originalSubMeshCount == 0) return; // Cannot clean mesh with no submeshes defined

        List<int[]> validSubmeshes = new List<int[]>();
        int totalCleanTrisCount = 0;

        for (int s = 0; s < originalSubMeshCount; s++)
        {
            int[] tris;
            try
            {
                tris = mesh.GetTriangles(s);
            }
            catch // Handle cases where submesh index is invalid
            {
                Debug.LogWarning($"[CleanMesh] Submesh {s} missing or invalid in {mesh.name}, skipping.");
                continue;
            }

            if (tris == null || tris.Length == 0) continue; // Skip empty submeshes

            // Remove degenerate triangles (zero area)
            List<int> cleanTris = new List<int>();
            for (int i = 0; i < tris.Length - 2; i += 3) // Iterate safely
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                // Check for valid indices
                if (a < 0 || b < 0 || c < 0 || a >= verts.Length || b >= verts.Length || c >= verts.Length)
                {
                    Debug.LogWarning($"[CleanMesh] Invalid triangle index found in submesh {s}. Skipping triangle.");
                    continue;
                }

                // Check for zero-area triangle
                Vector3 ab = verts[b] - verts[a];
                Vector3 ac = verts[c] - verts[a];
                if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-12f) // Use a small epsilon
                {
                    continue; // Skip degenerate triangle
                }

                cleanTris.Add(a);
                cleanTris.Add(b);
                cleanTris.Add(c);
            }

            if (cleanTris.Count > 0)
            {
                validSubmeshes.Add(cleanTris.ToArray());
                totalCleanTrisCount += cleanTris.Count;
            }
        }

        // If all triangles were degenerate, clear the mesh to avoid errors
        if (totalCleanTrisCount == 0)
        {
            Debug.LogWarning($"[CleanMesh] {mesh.name} resulted in an empty mesh after cleaning.");
            mesh.Clear();
            return;
        }

        // Apply cleaned triangles
        mesh.subMeshCount = validSubmeshes.Count;
        for (int s = 0; s < validSubmeshes.Count; s++)
        {
            mesh.SetTriangles(validSubmeshes[s], s);
        }

        // It might be beneficial to optimize the mesh after cleaning
        // mesh.Optimize(); // Consider if performance is impacted

        mesh.RecalculateBounds();

        Debug.Log($"[CleanMesh] {mesh.name} cleaned: verts={mesh.vertexCount}, submeshes={mesh.subMeshCount}, tris={totalCleanTrisCount / 3}");
    }

    // --- CORE SLICING LOGIC (mostly unchanged, kept for reference) ---

    private static void SeparateMeshes(GeneratedMesh leftMesh, GeneratedMesh rightMesh, Plane plane, List<Vector3> addedVertices)
    {
        if (originalMesh == null) return;
        int subMeshCount = originalMesh.subMeshCount;

        for (int i = 0; i < subMeshCount; i++)
        {
            int[] subMeshIndices;
            try { subMeshIndices = originalMesh.GetTriangles(i); }
            catch { continue; } // Skip invalid submesh index

            if (subMeshIndices == null || subMeshIndices.Length == 0) continue; // Skip empty submesh

            for (int j = 0; j < subMeshIndices.Length - 2; j += 3) // Iterate safely
            {
                int triangleIndexA = subMeshIndices[j];
                int triangleIndexB = subMeshIndices[j + 1];
                int triangleIndexC = subMeshIndices[j + 2];

                // Check for valid indices before accessing vertices/normals/uvs
                if (triangleIndexA < 0 || triangleIndexB < 0 || triangleIndexC < 0 ||
                    triangleIndexA >= originalMesh.vertexCount ||
                    triangleIndexB >= originalMesh.vertexCount ||
                    triangleIndexC >= originalMesh.vertexCount)
                {
                    Debug.LogWarning($"[SeparateMeshes] Invalid triangle index in submesh {i}. Skipping triangle.");
                    continue;
                }

                MeshTriangle currentTriangle = GetTriangle(triangleIndexA, triangleIndexB, triangleIndexC, i);
                if (currentTriangle == null) continue; // Skip if GetTriangle failed

                bool triangleALeftSide = plane.GetSide(originalMesh.vertices[triangleIndexA]);
                bool triangleBLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexB]);
                bool triangleCLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexC]);

                if (triangleALeftSide && triangleBLeftSide && triangleCLeftSide)
                {
                    leftMesh.AddTriangle(currentTriangle);
                }
                else if (!triangleALeftSide && !triangleBLeftSide && !triangleCLeftSide)
                {
                    rightMesh.AddTriangle(currentTriangle);
                }
                else
                {
                    CutTriangle(plane, currentTriangle, triangleALeftSide, triangleBLeftSide, triangleCLeftSide, leftMesh, rightMesh, addedVertices);
                }
            }
        }
    }

    private static MeshTriangle GetTriangle(int _triangleIndexA, int _triangleIndexB, int _triangleIndexC, int _submeshIndex)
    {
        // Add safety checks for index out of bounds
        if (_triangleIndexA >= originalMesh.vertexCount || _triangleIndexB >= originalMesh.vertexCount || _triangleIndexC >= originalMesh.vertexCount ||
            _triangleIndexA < 0 || _triangleIndexB < 0 || _triangleIndexC < 0)
        {
            Debug.LogError($"[GetTriangle] Invalid index! A:{_triangleIndexA}, B:{_triangleIndexB}, C:{_triangleIndexC} vs Verts:{originalMesh.vertexCount}");
            return null;
        }

        Vector3[] verticesToAdd = {
            originalMesh.vertices[_triangleIndexA],
            originalMesh.vertices[_triangleIndexB],
            originalMesh.vertices[_triangleIndexC]
        };

        // Check if normals exist
        Vector3[] normalsToAdd = new Vector3[3];
        if (originalMesh.normals != null && originalMesh.normals.Length > Mathf.Max(_triangleIndexA, _triangleIndexB, _triangleIndexC))
        {
            normalsToAdd[0] = originalMesh.normals[_triangleIndexA];
            normalsToAdd[1] = originalMesh.normals[_triangleIndexB];
            normalsToAdd[2] = originalMesh.normals[_triangleIndexC];
        }
        else
        {
            Vector3 defaultNormal = Vector3.up; // Or calculate from vertices
            normalsToAdd[0] = normalsToAdd[1] = normalsToAdd[2] = defaultNormal;
            Debug.LogWarning("[GetTriangle] Normals missing or index out of bounds, using default.");
        }


        // Check if UVs exist
        Vector2[] uvsToAdd = new Vector2[3];
        if (originalMesh.uv != null && originalMesh.uv.Length > Mathf.Max(_triangleIndexA, _triangleIndexB, _triangleIndexC))
        {
            uvsToAdd[0] = originalMesh.uv[_triangleIndexA];
            uvsToAdd[1] = originalMesh.uv[_triangleIndexB];
            uvsToAdd[2] = originalMesh.uv[_triangleIndexC];
        }
        else
        {
            uvsToAdd[0] = uvsToAdd[1] = uvsToAdd[2] = Vector2.zero; // Default UVs
                                                                    // Debug.LogWarning("[GetTriangle] UVs missing or index out of bounds, using default."); // Can be spammy
        }

        return new MeshTriangle(verticesToAdd, normalsToAdd, uvsToAdd, _submeshIndex);
    }

    private static void CutTriangle(Plane plane, MeshTriangle triangle, bool triangleALeftSide, bool triangleBLeftSide, bool triangleCLeftSide,
        GeneratedMesh leftMesh, GeneratedMesh rightMesh, List<Vector3> addedVertices)
    {
        List<bool> leftSide = new List<bool> { triangleALeftSide, triangleBLeftSide, triangleCLeftSide };

        // Indices of vertices on the left and right side
        List<int> leftIndices = new List<int>();
        List<int> rightIndices = new List<int>();

        for (int i = 0; i < 3; i++)
        {
            if (leftSide[i]) leftIndices.Add(i);
            else rightIndices.Add(i);
        }

        // Should always have vertices on both sides if we reach here
        if (leftIndices.Count == 0 || rightIndices.Count == 0)
        {
            Debug.LogError("[CutTriangle] Logic error: CutTriangle called but all vertices are on one side.");
            return;
        }

        // Simplified Cut Logic:
        // We find the two intersection points between the plane and the triangle edges crossing it.
        // Then we form new triangles based on which side has 1 or 2 vertices.

        Vector3 intersection1, intersection2;
        Vector3 normal1, normal2;
        Vector2 uv1, uv2;

        // Calculate intersections based on which vertex is alone
        if (leftIndices.Count == 1) // One vertex on the left, two on the right
        {
            int loneIndex = leftIndices[0];
            int otherIndex1 = rightIndices[0];
            int otherIndex2 = rightIndices[1];

            // Calculate intersection between loneIndex <-> otherIndex1
            CalculateIntersectionPoint(plane, triangle, loneIndex, otherIndex1, out intersection1, out normal1, out uv1);
            // Calculate intersection between loneIndex <-> otherIndex2
            CalculateIntersectionPoint(plane, triangle, loneIndex, otherIndex2, out intersection2, out normal2, out uv2);

            // Add the single triangle for the left side
            leftMesh.AddTriangle(
                new Vector3[] { triangle.Vertices[loneIndex], intersection1, intersection2 },
                new Vector3[] { triangle.Normals[loneIndex], normal1, normal2 },
                new Vector2[] { triangle.UVs[loneIndex], uv1, uv2 },
                triangle.SubmeshIndex
            );

            // Add the two triangles for the right side (quad)
            rightMesh.AddTriangle(
                new Vector3[] { intersection1, triangle.Vertices[otherIndex1], triangle.Vertices[otherIndex2] },
                new Vector3[] { normal1, triangle.Normals[otherIndex1], triangle.Normals[otherIndex2] },
                new Vector2[] { uv1, triangle.UVs[otherIndex1], triangle.UVs[otherIndex2] },
                triangle.SubmeshIndex
            );
            rightMesh.AddTriangle(
                new Vector3[] { intersection1, triangle.Vertices[otherIndex2], intersection2 },
                new Vector3[] { normal1, triangle.Normals[otherIndex2], normal2 },
                new Vector2[] { uv1, triangle.UVs[otherIndex2], uv2 },
                triangle.SubmeshIndex
            );

            addedVertices.Add(intersection1);
            addedVertices.Add(intersection2);
        }
        else // Two vertices on the left, one on the right
        {
            int loneIndex = rightIndices[0];
            int otherIndex1 = leftIndices[0];
            int otherIndex2 = leftIndices[1];

            // Calculate intersection between loneIndex <-> otherIndex1
            CalculateIntersectionPoint(plane, triangle, loneIndex, otherIndex1, out intersection1, out normal1, out uv1);
            // Calculate intersection between loneIndex <-> otherIndex2
            CalculateIntersectionPoint(plane, triangle, loneIndex, otherIndex2, out intersection2, out normal2, out uv2);

            // Add the two triangles for the left side (quad)
            leftMesh.AddTriangle(
                new Vector3[] { intersection1, triangle.Vertices[otherIndex1], triangle.Vertices[otherIndex2] },
                new Vector3[] { normal1, triangle.Normals[otherIndex1], triangle.Normals[otherIndex2] },
                new Vector2[] { uv1, triangle.UVs[otherIndex1], triangle.UVs[otherIndex2] },
                triangle.SubmeshIndex
            );
            leftMesh.AddTriangle(
                new Vector3[] { intersection1, triangle.Vertices[otherIndex2], intersection2 },
                new Vector3[] { normal1, triangle.Normals[otherIndex2], normal2 },
                new Vector2[] { uv1, triangle.UVs[otherIndex2], uv2 },
                triangle.SubmeshIndex
            );

            // Add the single triangle for the right side
            rightMesh.AddTriangle(
                new Vector3[] { triangle.Vertices[loneIndex], intersection1, intersection2 },
                new Vector3[] { triangle.Normals[loneIndex], normal1, normal2 },
                new Vector2[] { triangle.UVs[loneIndex], uv1, uv2 },
                triangle.SubmeshIndex
            );

            addedVertices.Add(intersection1);
            addedVertices.Add(intersection2);
        }
    }

    // Helper to calculate intersection point and interpolate attributes
    private static void CalculateIntersectionPoint(Plane plane, MeshTriangle triangle, int indexA, int indexB,
        out Vector3 intersectionPoint, out Vector3 intersectionNormal, out Vector2 intersectionUv)
    {
        Vector3 vertexA = triangle.Vertices[indexA];
        Vector3 vertexB = triangle.Vertices[indexB];
        Vector3 direction = (vertexB - vertexA);

        float distance;
        if (plane.Raycast(new Ray(vertexA, direction.normalized), out distance))
        {
            float normalizedDistance = distance / direction.magnitude;
            // Clamp distance to avoid issues with floating point inaccuracies near the plane
            normalizedDistance = Mathf.Clamp01(normalizedDistance);

            intersectionPoint = Vector3.Lerp(vertexA, vertexB, normalizedDistance);
            intersectionNormal = Vector3.Lerp(triangle.Normals[indexA], triangle.Normals[indexB], normalizedDistance);
            intersectionUv = Vector2.Lerp(triangle.UVs[indexA], triangle.UVs[indexB], normalizedDistance);
        }
        else // Raycast failed (likely parallel or point on plane), use midpoint as fallback
        {
            Debug.LogWarning("[CutTriangle] Raycast failed. Using midpoint.");
            intersectionPoint = (vertexA + vertexB) / 2f;
            intersectionNormal = (triangle.Normals[indexA] + triangle.Normals[indexB]).normalized;
            intersectionUv = (triangle.UVs[indexA] + triangle.UVs[indexB]) / 2f;
        }
    }


    private static void FlipTriangel(MeshTriangle _triangle)
    {
        // Swap vertices 0 and 2
        Vector3 tempVert = _triangle.Vertices[2];
        _triangle.Vertices[2] = _triangle.Vertices[0];
        _triangle.Vertices[0] = tempVert;

        // Swap normals 0 and 2
        Vector3 tempNorm = _triangle.Normals[2];
        _triangle.Normals[2] = _triangle.Normals[0];
        _triangle.Normals[0] = tempNorm;

        // Swap UVs 0 and 2
        Vector2 tempUv = _triangle.UVs[2];
        _triangle.UVs[2] = _triangle.UVs[0];
        _triangle.UVs[0] = tempUv;
    }

    public static void FillCut(List<Vector3> _addedVertices, Plane _plane, GeneratedMesh _leftMesh, GeneratedMesh _rightMesh)
    {
        if (_addedVertices == null || _addedVertices.Count < 2) return;

        List<Vector3> edgeVertices = new List<Vector3>(_addedVertices);
        List<Vector3> polygon = new List<Vector3>();
        List<int> polygonIndices = new List<int>(); // To use for triangulation

        // --- Simplified Polygon Finding ---
        // This attempts to sort vertices based on angle around the center point.
        // It's not perfectly robust for complex cuts but often works for convex shapes.

        if (edgeVertices.Count < 3) return; // Cannot triangulate less than 3 points

        // Calculate center
        Vector3 center = Vector3.zero;
        foreach (Vector3 v in edgeVertices) center += v;
        center /= edgeVertices.Count;

        // Calculate reference vectors for sorting
        Vector3 planeNormal = _plane.normal;
        Vector3 referenceVec = (edgeVertices[0] - center).normalized;
        Vector3 upVec = planeNormal; // Use plane normal as 'up'
        Vector3 rightVec = Vector3.Cross(upVec, referenceVec).normalized;

        // Sort vertices by angle
        edgeVertices.Sort((a, b) =>
        {
            Vector3 dirA = (a - center).normalized;
            Vector3 dirB = (b - center).normalized;
            float angleA = Mathf.Atan2(Vector3.Dot(dirA, rightVec), Vector3.Dot(dirA, referenceVec));
            float angleB = Mathf.Atan2(Vector3.Dot(dirB, rightVec), Vector3.Dot(dirB, referenceVec));
            return angleA.CompareTo(angleB);
        });

        polygon = edgeVertices; // Use sorted vertices as the polygon

        // --- Basic Fan Triangulation ---
        if (polygon.Count >= 3)
        {
            Vector3 polyNormal = -_plane.normal; // Normal for the filled face
            Vector2 centerUv = new Vector2(0.5f, 0.5f); // UV for the center point

            for (int i = 1; i < polygon.Count - 1; i++)
            {
                Vector3 v0 = polygon[0];
                Vector3 v1 = polygon[i];
                Vector3 v2 = polygon[i + 1];

                // Calculate basic UVs based on projection (simple but might stretch)
                Vector2 uv0 = GetPlanarUv(v0, center, upVec, rightVec);
                Vector2 uv1 = GetPlanarUv(v1, center, upVec, rightVec);
                Vector2 uv2 = GetPlanarUv(v2, center, upVec, rightVec);

                // Add to Left Mesh (facing opposite plane normal)
                _leftMesh.AddTriangle(
                    new Vector3[] { v0, v1, v2 },
                    new Vector3[] { polyNormal, polyNormal, polyNormal },
                    new Vector2[] { uv0, uv1, uv2 },
                    0 // Use submesh 0 for the fill, or add a dedicated submesh index
                );

                // Add to Right Mesh (facing plane normal) - reversed triangle order
                _rightMesh.AddTriangle(
                    new Vector3[] { v0, v2, v1 }, // Reversed order
                    new Vector3[] { -polyNormal, -polyNormal, -polyNormal }, // Flipped normal
                    new Vector2[] { uv0, uv2, uv1 }, // Reversed UVs to match
                    0 // Use submesh 0 for the fill
                );
            }
        }
    }

    // Helper to get basic planar UVs
    private static Vector2 GetPlanarUv(Vector3 vertex, Vector3 center, Vector3 up, Vector3 right)
    {
        Vector3 displacement = vertex - center;
        // Project onto the plane defined by up and right, scale as needed
        float u = Vector3.Dot(displacement, right) * 0.1f + 0.5f; // Example scaling
        float v = Vector3.Dot(displacement, up) * 0.1f + 0.5f;
        return new Vector2(u, v);
    }


    // --- Deprecated EvaluatePairs and old Fill - kept for reference if needed ---
    /*
    public static void EvaluatePairs(List<Vector3> _addedVertices, List<Vector3> _vertices, List<Vector3> _polygone)
    {
        // ... old logic ...
    }

    private static void Fill(List<Vector3> _vertices, Plane _plane, GeneratedMesh _leftMesh, GeneratedMesh _rightMesh)
    {
        // ... old logic ...
    }
    */
}
