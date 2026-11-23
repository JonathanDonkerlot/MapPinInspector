using Alta.Map;
using MapPinInspector.Core;
using UnityEngine;

namespace MapPinInspector
{
    /// <summary>
    /// Converts world XZ marker positions into exact 3D points and outward normals on a Map Board's Renderer Plane.
    /// </summary>
    public sealed class BoardSurfaceMapper
    {
        private enum PlaneFallbackAxes
        {
            XY,
            XZ,
            YZ
        }

        private readonly MapBoard _mapBoard;
        private readonly Transform _surfaceTransform;
        private readonly Renderer _previewRenderer;

        private bool _hasMeshGeometry;
        private Vec2[] _meshUvs;
        private Vec3[] _meshVerts;
        private int[] _meshTris;

        private Bounds _fallbackLocalBounds;
        private PlaneFallbackAxes _fallbackAxes;
        private Vector3 _localNormalHint;

        public Transform SurfaceTransform => _surfaceTransform;
        public Vector3 WorldCenter => _previewRenderer != null ? _previewRenderer.bounds.center : _surfaceTransform.position;

        public BoardSurfaceMapper(MapBoard mapBoard, Transform surfaceTransform, Renderer previewRenderer)
        {
            _mapBoard = mapBoard;
            _surfaceTransform = surfaceTransform;
            _previewRenderer = previewRenderer;

            InitializeGeometry();
        }

        private void InitializeGeometry()
        {
            MeshFilter mf = _surfaceTransform != null ? _surfaceTransform.GetComponent<MeshFilter>() : null;
            Mesh mesh = mf != null ? mf.sharedMesh : null;

            if (mesh != null && mesh.isReadable && mesh.vertexCount >= 3)
            {
                Vector2[] uvs = mesh.uv;
                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;

                if (uvs != null && verts != null && tris != null &&
                    uvs.Length == verts.Length && tris.Length >= 3)
                {
                    _meshUvs = new Vec2[uvs.Length];
                    _meshVerts = new Vec3[verts.Length];
                    for (int i = 0; i < verts.Length; i++)
                    {
                        _meshUvs[i] = new Vec2(uvs[i].x, uvs[i].y);
                        _meshVerts[i] = new Vec3(verts[i].x, verts[i].y, verts[i].z);
                    }
                    _meshTris = tris;
                    _hasMeshGeometry = true;
                }
            }

            _fallbackLocalBounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector3 size = _fallbackLocalBounds.size;

            if (size.x <= size.y && size.x <= size.z)
            {
                _fallbackAxes = PlaneFallbackAxes.YZ;
                _localNormalHint = Vector3.right;
            }
            else if (size.y <= size.x && size.y <= size.z)
            {
                _fallbackAxes = PlaneFallbackAxes.XZ;
                _localNormalHint = Vector3.up;
            }
            else
            {
                _fallbackAxes = PlaneFallbackAxes.XY;
                _localNormalHint = -Vector3.forward;
            }

            _localNormalHint = OrientLocalNormalTowardFront(_localNormalHint);
        }

        private Vector3 OrientLocalNormalTowardFront(Vector3 candidateLocalNormal)
        {
            if (_surfaceTransform == null)
                return candidateLocalNormal.normalized;

            Vector3 worldCandidate = _surfaceTransform.TransformDirection(candidateLocalNormal).normalized;

            Transform frontRef = null;
            if (_mapBoard != null)
            {
                HandHoverPoint hhp = _mapBoard.GetComponentInChildren<HandHoverPoint>(true);
                if (hhp != null)
                    frontRef = hhp.transform;
                else
                    frontRef = _mapBoard.transform.Find("Hand Touch");
            }

            Vector3 referenceFrontDir;
            if (frontRef != null)
            {
                Vector3 delta = frontRef.position - WorldCenter;
                referenceFrontDir = delta.sqrMagnitude > 1e-6f ? delta.normalized : -_surfaceTransform.forward;
            }
            else
            {
                // ATT MapBoardRenderer / WorldSpace Quad faces -forward in Renderer Plane space.
                referenceFrontDir = -_surfaceTransform.forward;
            }

            if (Vector3.Dot(worldCandidate, referenceFrontDir) < 0f)
                return -candidateLocalNormal.normalized;

            return candidateLocalNormal.normalized;
        }

        public bool TryProjectWorldPosition(
            Vector2 worldXZ,
            out Vector3 localPoint,
            out Vector3 localNormal,
            out Vector3 worldPoint,
            out Vector3 worldNormal)
        {
            localPoint = Vector3.zero;
            localNormal = _localNormalHint;
            worldPoint = Vector3.zero;
            worldNormal = Vector3.forward;

            if (_mapBoard == null || _surfaceTransform == null)
                return false;

            if (_mapBoard.Map == null)
                return false;

            Vector2 mapPos = _mapBoard.WorldToMapPosition(worldXZ);
            Vector4 offsetScale = _mapBoard.MapOffsetScale;
            Vec2 uv = MapProjectionMath.MapPositionToUv(
                new Vec2(mapPos.x, mapPos.y),
                new Vec2(offsetScale.x, offsetScale.y));

            if (!MapProjectionMath.IsUvInsideBoard(uv, 0.01f))
                return false;

            if (_hasMeshGeometry &&
                MapProjectionMath.TryProjectUvToMeshLocal(
                    uv,
                    _meshUvs,
                    _meshVerts,
                    _meshTris,
                    out Vec3 meshLocalPos,
                    out Vec3 meshLocalNorm))
            {
                localPoint = new Vector3(meshLocalPos.X, meshLocalPos.Y, meshLocalPos.Z);
                Vector3 normCandidate = new Vector3(meshLocalNorm.X, meshLocalNorm.Y, meshLocalNorm.Z);
                localNormal = OrientLocalNormalTowardFront(normCandidate);
            }
            else
            {
                localPoint = MapUvToBoundsLocal(uv);
                localNormal = _localNormalHint;
            }

            worldPoint = _surfaceTransform.TransformPoint(localPoint);
            worldNormal = _surfaceTransform.TransformDirection(localNormal).normalized;
            return true;
        }

        private Vector3 MapUvToBoundsLocal(Vec2 uv)
        {
            Vector3 min = _fallbackLocalBounds.min;
            Vector3 size = _fallbackLocalBounds.size;
            Vector3 center = _fallbackLocalBounds.center;

            switch (_fallbackAxes)
            {
                case PlaneFallbackAxes.YZ:
                    return new Vector3(center.x, min.y + uv.Y * size.y, min.z + uv.X * size.z);
                case PlaneFallbackAxes.XZ:
                    return new Vector3(min.x + uv.X * size.x, center.y, min.z + uv.Y * size.z);
                default:
                    return new Vector3(min.x + uv.X * size.x, min.y + uv.Y * size.y, center.z);
            }
        }

        public float DistanceToBoard(Vector3 worldPos)
        {
            if (_previewRenderer != null)
            {
                Vector3 closest = _previewRenderer.bounds.ClosestPoint(worldPos);
                return Vector3.Distance(worldPos, closest);
            }

            return _surfaceTransform != null
                ? Vector3.Distance(worldPos, _surfaceTransform.position)
                : float.MaxValue;
        }
    }
}
