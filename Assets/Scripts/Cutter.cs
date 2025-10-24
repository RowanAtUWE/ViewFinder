using System.Collections.Generic;
using UnityEngine;

/*
As mentioned in the video, this is a custom implementation based on Kristin Lague's project. 
If you've downloaded this repo for the cutting part, I suggest starting with this repo: https://github.com/KristinLague/Mesh-Cutting
and this video: https://www.youtube.com/watch?v=1UsuZsaUUng&t=7s 
*/

public class Cutter : MonoBehaviour
{
    private static bool isBusy;
    private static Mesh originalMesh;

    public static GameObject Cut(GameObject originalGameObject, Vector3 contactPoint, Vector3 cutNormal)
    {
        if (isBusy)
            return null;

        isBusy = true;

        Plane cutPlane = new Plane(
            originalGameObject.transform.InverseTransformDirection(-cutNormal),
            originalGameObject.transform.InverseTransformPoint(contactPoint)
        );

        originalMesh = originalGameObject.GetComponent<MeshFilter>().mesh;

        if (originalMesh == null)
        {
            Debug.LogError("Need mesh to cut");
            isBusy = false;
            return null;
        }

        // Capture original materials early so we can copy them exactly
        var originalRenderer = originalGameObject.GetComponent<MeshRenderer>();
        Material[] originalMaterials = null;
        if (originalRenderer != null && originalRenderer.sharedMaterials != null && originalRenderer.sharedMaterials.Length > 0)
            originalMaterials = originalRenderer.sharedMaterials;
        else
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

        var rightRenderer = right.AddComponent<MeshRenderer>();
        Material[] rightMats = BuildMaterialArrayForMesh(originalMaterials, finishedRightMesh.subMeshCount);
        ApplyColliderAndMaterials(right, finishedRightMesh, rightMats);

        var rightRb = right.AddComponent<Rigidbody>();
        rightRb.isKinematic = true;

        right.layer = LayerMask.NameToLayer("Cuttable");

        isBusy = false;
        return right;
    }

    private static Material[] BuildMaterialArrayForMesh(Material[] sourceMaterials, int meshSubMeshCount)
    {
        int targetCount = Mathf.Max(1, meshSubMeshCount);
        Material[] mats = new Material[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            // copy source material by index, or wrap around, or use first as fallback
            if (sourceMaterials != null && sourceMaterials.Length > 0)
                mats[i] = sourceMaterials[i % sourceMaterials.Length] ?? new Material(Shader.Find("Standard"));
            else
                mats[i] = new Material(Shader.Find("Standard"));
        }

        return mats;
    }

    private static void ApplyColliderAndMaterials(GameObject go, Mesh mesh, Material[] mats)
    {
        if (go == null) return;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();

        // assign materials (use sharedMaterials to preserve shader references)
        renderer.sharedMaterials = mats;

        // Add MeshCollider safely
        var existingCollider = go.GetComponent<MeshCollider>();
        if (existingCollider != null)
        {
            existingCollider.sharedMesh = null;
            Destroy(existingCollider);
        }

        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;

        try
        {
            col.convex = true;
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

        if (mesh.vertexCount > 65000)
        {
            try { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
            catch { /* ignore if unsupported */ }
        }

        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
    }

    private static void CleanMesh(Mesh mesh)
    {
        if (mesh == null) return;

        Vector3[] verts = mesh.vertices;
        int originalSubMeshCount = Mathf.Max(1, mesh.subMeshCount);

        // Track valid submeshes
        List<int[]> validSubmeshes = new List<int[]>();

        for (int s = 0; s < originalSubMeshCount; s++)
        {
            int[] tris;
            try
            {
                tris = mesh.GetTriangles(s);
            }
            catch
            {
                Debug.LogWarning($"[CleanMesh] Submesh {s} missing in {mesh.name}, skipping.");
                continue;
            }

            // Remove degenerate triangles
            List<int> cleanTris = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= verts.Length || b >= verts.Length || c >= verts.Length)
                    continue;

                Vector3 ab = verts[b] - verts[a];
                Vector3 ac = verts[c] - verts[a];
                if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-10f)
                    continue;

                cleanTris.Add(a);
                cleanTris.Add(b);
                cleanTris.Add(c);
            }

            if (cleanTris.Count > 0)
                validSubmeshes.Add(cleanTris.ToArray());
        }

        // Apply cleaned triangles
        mesh.subMeshCount = validSubmeshes.Count;
        for (int s = 0; s < validSubmeshes.Count; s++)
            mesh.SetTriangles(validSubmeshes[s], s);

        mesh.RecalculateBounds();

        // Optional debug
        Debug.Log($"[CleanMesh] {mesh.name} cleaned: verts={mesh.vertexCount}, submeshes={mesh.subMeshCount}");
    }

    private static void SeparateMeshes(GeneratedMesh leftMesh, GeneratedMesh rightMesh, Plane plane, List<Vector3> addedVertices)
    {
        for (int i = 0; i < originalMesh.subMeshCount; i++)
        {
            int[] subMeshIndices;
            try { subMeshIndices = originalMesh.GetTriangles(i); }
            catch { continue; }

            for (int j = 0; j < subMeshIndices.Length; j += 3)
            {
                var triangleIndexA = subMeshIndices[j];
                var triangleIndexB = subMeshIndices[j + 1];
                var triangleIndexC = subMeshIndices[j + 2];

                MeshTriangle currentTriangle = GetTriangle(triangleIndexA, triangleIndexB, triangleIndexC, i);

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
        Vector3[] verticesToAdd = {
            originalMesh.vertices[_triangleIndexA],
            originalMesh.vertices[_triangleIndexB],
            originalMesh.vertices[_triangleIndexC]
        };

        Vector3[] normalsToAdd = {
            originalMesh.normals[_triangleIndexA],
            originalMesh.normals[_triangleIndexB],
            originalMesh.normals[_triangleIndexC]
        };

        Vector2[] uvsToAdd = {
            originalMesh.uv[_triangleIndexA],
            originalMesh.uv[_triangleIndexB],
            originalMesh.uv[_triangleIndexC]
        };

        return new MeshTriangle(verticesToAdd, normalsToAdd, uvsToAdd, _submeshIndex);
    }

    private static void CutTriangle(Plane plane, MeshTriangle triangle, bool triangleALeftSide, bool triangleBLeftSide, bool triangleCLeftSide,
        GeneratedMesh leftMesh, GeneratedMesh rightMesh, List<Vector3> addedVertices)
    {
        List<bool> leftSide = new List<bool>();
        leftSide.Add(triangleALeftSide);
        leftSide.Add(triangleBLeftSide);
        leftSide.Add(triangleCLeftSide);

        MeshTriangle leftMeshTriangle = new MeshTriangle(new Vector3[2], new Vector3[2], new Vector2[2], triangle.SubmeshIndex);
        MeshTriangle rightMeshTriangle = new MeshTriangle(new Vector3[2], new Vector3[2], new Vector2[2], triangle.SubmeshIndex);

        bool left = false;
        bool right = false;

        for (int i = 0; i < 3; i++)
        {
            if (leftSide[i])
            {
                if (!left)
                {
                    left = true;

                    leftMeshTriangle.Vertices[0] = triangle.Vertices[i];
                    leftMeshTriangle.Vertices[1] = leftMeshTriangle.Vertices[0];

                    leftMeshTriangle.UVs[0] = triangle.UVs[i];
                    leftMeshTriangle.UVs[1] = leftMeshTriangle.UVs[0];

                    leftMeshTriangle.Normals[0] = triangle.Normals[i];
                    leftMeshTriangle.Normals[1] = leftMeshTriangle.Normals[0];
                }
                else
                {
                    leftMeshTriangle.Vertices[1] = triangle.Vertices[i];
                    leftMeshTriangle.Normals[1] = triangle.Normals[i];
                    leftMeshTriangle.UVs[1] = triangle.UVs[i];
                }
            }
            else
            {
                if (!right)
                {
                    right = true;

                    rightMeshTriangle.Vertices[0] = triangle.Vertices[i];
                    rightMeshTriangle.Vertices[1] = rightMeshTriangle.Vertices[0];

                    rightMeshTriangle.UVs[0] = triangle.UVs[i];
                    rightMeshTriangle.UVs[1] = rightMeshTriangle.UVs[0];

                    rightMeshTriangle.Normals[0] = triangle.Normals[i];
                    rightMeshTriangle.Normals[1] = rightMeshTriangle.Normals[0];

                }
                else
                {
                    rightMeshTriangle.Vertices[1] = triangle.Vertices[i];
                    rightMeshTriangle.Normals[1] = triangle.Normals[i];
                    rightMeshTriangle.UVs[1] = triangle.UVs[i];
                }
            }
        }

        float normalizedDistance;
        float distance;
        // First intersection
        plane.Raycast(new Ray(leftMeshTriangle.Vertices[0], (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).normalized), out distance);
        normalizedDistance = distance / (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).magnitude;
        if (float.IsNaN(normalizedDistance) || float.IsInfinity(normalizedDistance)) normalizedDistance = 0.5f;
        Vector3 vertLeft = Vector3.Lerp(leftMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[0], normalizedDistance);
        addedVertices.Add(vertLeft);

        Vector3 normalLeft = Vector3.Lerp(leftMeshTriangle.Normals[0], rightMeshTriangle.Normals[0], normalizedDistance);
        Vector2 uvLeft = Vector2.Lerp(leftMeshTriangle.UVs[0], rightMeshTriangle.UVs[0], normalizedDistance);

        // Second intersection
        plane.Raycast(new Ray(leftMeshTriangle.Vertices[1], (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).normalized), out distance);
        normalizedDistance = distance / (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).magnitude;
        if (float.IsNaN(normalizedDistance) || float.IsInfinity(normalizedDistance)) normalizedDistance = 0.5f;
        Vector3 vertRight = Vector3.Lerp(leftMeshTriangle.Vertices[1], rightMeshTriangle.Vertices[1], normalizedDistance);
        addedVertices.Add(vertRight);

        Vector3 normalRight = Vector3.Lerp(leftMeshTriangle.Normals[1], rightMeshTriangle.Normals[1], normalizedDistance);
        Vector2 uvRight = Vector2.Lerp(leftMeshTriangle.UVs[1], rightMeshTriangle.UVs[1], normalizedDistance);

        // FIRST TRIANGLE (left)
        MeshTriangle currentTriangle;
        Vector3[] updatedVertices = { leftMeshTriangle.Vertices[0], vertLeft, vertRight };
        Vector3[] updatedNormals = { leftMeshTriangle.Normals[0], normalLeft, normalRight };
        Vector2[] updatedUVs = { leftMeshTriangle.UVs[0], uvLeft, uvRight };

        currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
        if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
        {
            if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
            {
                FlipTriangel(currentTriangle);
            }
            leftMesh.AddTriangle(currentTriangle);
        }

        // SECOND TRIANGLE (left)
        updatedVertices = new Vector3[] { leftMeshTriangle.Vertices[0], leftMeshTriangle.Vertices[1], vertRight };
        updatedNormals = new Vector3[] { leftMeshTriangle.Normals[0], leftMeshTriangle.Normals[1], normalRight };
        updatedUVs = new Vector2[] { leftMeshTriangle.UVs[0], leftMeshTriangle.UVs[1], uvRight };

        currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
        if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
        {
            if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
            {
                FlipTriangel(currentTriangle);
            }
            leftMesh.AddTriangle(currentTriangle);
        }

        // THIRD TRIANGLE (right)
        updatedVertices = new Vector3[] { rightMeshTriangle.Vertices[0], vertLeft, vertRight };
        updatedNormals = new Vector3[] { rightMeshTriangle.Normals[0], normalLeft, normalRight };
        updatedUVs = new Vector2[] { rightMeshTriangle.UVs[0], uvLeft, uvRight };

        currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
        if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
        {
            if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
            {
                FlipTriangel(currentTriangle);
            }
            rightMesh.AddTriangle(currentTriangle);
        }

        // FOURTH TRIANGLE (right)
        updatedVertices = new Vector3[] { rightMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[1], vertRight };
        updatedNormals = new Vector3[] { rightMeshTriangle.Normals[0], rightMeshTriangle.Normals[1], normalRight };
        updatedUVs = new Vector2[] { rightMeshTriangle.UVs[0], rightMeshTriangle.UVs[1], uvRight };

        currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
        if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
        {
            if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
            {
                FlipTriangel(currentTriangle);
            }
            rightMesh.AddTriangle(currentTriangle);
        }
    }

    private static void FlipTriangel(MeshTriangle _triangle)
    {
        Vector3 temp = _triangle.Vertices[2];
        _triangle.Vertices[2] = _triangle.Vertices[0];
        _triangle.Vertices[0] = temp;

        temp = _triangle.Normals[2];
        _triangle.Normals[2] = _triangle.Normals[0];
        _triangle.Normals[0] = temp;

        (_triangle.UVs[2], _triangle.UVs[0]) = (_triangle.UVs[0], _triangle.UVs[2]);
    }

    public static void FillCut(List<Vector3> _addedVertices, Plane _plane, GeneratedMesh _leftMesh, GeneratedMesh _rightMesh)
    {
        if (_addedVertices == null || _addedVertices.Count < 2) return;

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> polygon = new List<Vector3>();

        for (int i = 0; i < _addedVertices.Count - 1; i++)
        {
            if (!vertices.Contains(_addedVertices[i]))
            {
                polygon.Clear();
                polygon.Add(_addedVertices[i]);
                polygon.Add(_addedVertices[i + 1]);

                vertices.Add(_addedVertices[i]);
                vertices.Add(_addedVertices[i + 1]);

                EvaluatePairs(_addedVertices, vertices, polygon);

                if (polygon.Count >= 3)
                    Fill(polygon, _plane, _leftMesh, _rightMesh);
            }
        }
    }

    public static void EvaluatePairs(List<Vector3> _addedVertices, List<Vector3> _vertices, List<Vector3> _polygone)
    {
        bool isDone = false;
        int safety = 0;
        int maxIterations = Mathf.Max(1000, _addedVertices.Count * 10);

        while (!isDone && safety++ < maxIterations)
        {
            isDone = true;
            for (int i = 0; i < _addedVertices.Count - 1; i += 2)
            {
                Vector3 last = _polygone[_polygone.Count - 1];
                if (_addedVertices[i] == last && !_vertices.Contains(_addedVertices[i + 1]))
                {
                    isDone = false;
                    _polygone.Add(_addedVertices[i + 1]);
                    _vertices.Add(_addedVertices[i + 1]);
                }
                else if (_addedVertices[i + 1] == last && !_vertices.Contains(_addedVertices[i]))
                {
                    isDone = false;
                    _polygone.Add(_addedVertices[i]);
                    _vertices.Add(_addedVertices[i]);
                }
            }
        }

        if (safety >= maxIterations)
            Debug.LogWarning("[EvaluatePairs] reached safety iteration limit - polygon may be malformed.");
    }

    private static void Fill(List<Vector3> _vertices, Plane _plane, GeneratedMesh _leftMesh, GeneratedMesh _rightMesh)
    {
        if (_vertices == null || _vertices.Count < 3) return;

        Vector3 centerPosition = Vector3.zero;
        for (int i = 0; i < _vertices.Count; i++)
        {
            centerPosition += _vertices[i];
        }
        centerPosition /= _vertices.Count;

        Vector3 up = _plane.normal;
        Vector3 left = Vector3.Cross(_plane.normal, up);

        for (int i = 0; i < _vertices.Count; i++)
        {
            Vector3 displacement = _vertices[i] - centerPosition;
            Vector2 uv1 = new Vector2(.5f + Vector3.Dot(displacement, left), .5f + Vector3.Dot(displacement, up));

            displacement = _vertices[(i + 1) % _vertices.Count] - centerPosition;
            Vector2 uv2 = new Vector2(.5f + Vector3.Dot(displacement, left), .5f + Vector3.Dot(displacement, up));

            Vector3[] vertices = { _vertices[i], _vertices[(i + 1) % _vertices.Count], centerPosition };
            Vector3[] normals = { -_plane.normal, -_plane.normal, -_plane.normal };
            Vector2[] uvs = { uv1, uv2, new Vector2(0.5f, 0.5f) };

            MeshTriangle currentTriangle = new MeshTriangle(vertices, normals, uvs, 0);

            if (Vector3.Dot(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]), normals[0]) < 0)
            {
                FlipTriangel(currentTriangle);
            }
            _leftMesh.AddTriangle(currentTriangle);

            normals = new[] { _plane.normal, _plane.normal, _plane.normal };
            currentTriangle = new MeshTriangle(vertices, normals, uvs, originalMesh.subMeshCount + 1);

            if (Vector3.Dot(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]), normals[0]) < 0)
            {
                FlipTriangel(currentTriangle);
            }
            _rightMesh.AddTriangle(currentTriangle);
        }
    }
}
