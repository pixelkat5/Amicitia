using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Graphics;
using OpenTK.Input;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using AmicitiaLibrary.Graphics.RenderWare;
using AmicitiaLibrary.PS2.Graphics;
using SN = System.Numerics;

namespace Amicitia.ModelViewer
{
    public struct GpuModel
    {
        public int Vbo;
        public int[] Ibo;
        public int Vc;
        public int[] Ic;
        public string[] Tid;
        public Color[] Color;
        public bool Strip;
        public GpuModel(int vbo, int[] ibo, int vc, int[] ic, string[] tid, Color[] color, bool strip)
        {
            this.Vbo = vbo;
            this.Ibo = ibo;
            this.Vc = vc;
            this.Ic = ic;
            this.Tid = tid;
            this.Color = color;
            this.Strip = strip;
        }
    }

    public struct BasicCol4
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public BasicCol4(Color color)
        {
            R = color.R;
            G = color.G;
            B = color.B;
            A = color.A;
        }
    }

    public struct BasicVec4
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public BasicVec4(float x, float y, float z, float w)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.W = w;
        }

        public BasicVec4(Vector4 v)
        {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
            W = v.W;
        }

        public BasicVec4(Color4 v)
        {
            X = v.R;
            Y = v.G;
            Z = v.B;
            W = v.A;
        }
    }

    public struct BasicVec3
    {
        public float X;
        public float Y;
        public float Z;

        public BasicVec3(float x, float y, float z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public BasicVec3(SN.Vector3 v)
        {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
        }
    }

    public struct BasicVec2
    {
        public float X, Y;

        public BasicVec2(float x, float y)
        {
            this.X = x;
            this.Y = y;
        }

        public BasicVec2(SN.Vector2 v)
        {
            X = v.X;
            Y = v.Y;
        }

    }

    public struct Vertex
    {
        public BasicVec3 Pos; 
        public BasicVec3 Nrm; 
        public BasicVec2 Tex;
        public BasicVec4 Col;

        public Vertex(BasicVec3 pos, BasicVec3 nrm, BasicVec2 tex, BasicVec4 col)
        {
            this.Pos = pos;
            this.Nrm = nrm;
            this.Tex = tex;
            this.Col = col;
        }
    }

    public class Camera
    {
        // Yaw = rotation around Y axis (left/right)
        // Pitch = rotation around X axis (up/down)
        private float mYaw   = -90f; // facing -Z by default
        private float mPitch =   0f;

        public Camera()
        {
            Position = new Vector3(0, 100, 250);
            Up       = new Vector3(0, 1, 0);
            UpdateForward();
        }

        public Vector3 Position { get; set; }
        public Vector3 Forward  { get; private set; }
        public Vector3 Up       { get; private set; }
        public Vector3 Right    => Vector3.Normalize(Vector3.Cross(Forward, Up));

        public void Rotate(float dx, float dy, float sensitivity = 0.2f)
        {
            mYaw   += dx * sensitivity;
            mPitch -= dy * sensitivity;          // subtract: dragging down looks up
            mPitch  = MathHelper.Clamp(mPitch, -89f, 89f);
            UpdateForward();
        }

        private void UpdateForward()
        {
            float yawRad   = MathHelper.DegreesToRadians(mYaw);
            float pitchRad = MathHelper.DegreesToRadians(mPitch);
            Forward = Vector3.Normalize(new Vector3(
                (float)(Math.Cos(pitchRad) * Math.Cos(yawRad)),
                (float) Math.Sin(pitchRad),
                (float)(Math.Cos(pitchRad) * Math.Sin(yawRad))
            ));
        }

        public void Bind(ShaderProgram shader, GLControl control)
        {
            Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60.0f), (float)control.Width / control.Height, 0.1f, 100000.0f);
            Matrix4 view = Matrix4.LookAt(Position, Position + Forward, Up);
            shader.SetUniform("proj", proj);
            shader.SetUniform("view", view);
        }
    }

    internal static class RwtoGlConversionHelper
    {
        public static Dictionary<PS2FilterMode, int> FilterDictionary { get; } = new Dictionary<PS2FilterMode, int>()
        {
            { PS2FilterMode.None,               (int)TextureMagFilter.Linear },
            { PS2FilterMode.Nearest,            (int)TextureMagFilter.Nearest },
            { PS2FilterMode.Linear,             (int)TextureMagFilter.Linear },
            { PS2FilterMode.MipNearest,         (int)TextureMagFilter.Nearest },
            { PS2FilterMode.MipLinear,          (int)TextureMagFilter.Linear },
            { PS2FilterMode.LinearMipNearest,   (int)TextureMagFilter.Nearest },
            { PS2FilterMode.LinearMipLinear,    (int)TextureMagFilter.Linear }
        };

        public static Dictionary<PS2AddressingMode, int> WrapDictionary { get; } = new Dictionary<PS2AddressingMode, int>()
        {
            { PS2AddressingMode.None,       (int)TextureWrapMode.Repeat },
            { PS2AddressingMode.Wrap,       (int)TextureWrapMode.Repeat },
            { PS2AddressingMode.Mirror,     (int)TextureWrapMode.MirroredRepeat },
            { PS2AddressingMode.Clamp,      (int)TextureWrapMode.ClampToBorder }
        };
    }

    public class ModelViewer
    {
        // view states
        private bool mCanRender;
        private bool mIsViewReady;
        private bool mIsViewFocused;

        // store a handle to the loaded scene

        // gl control handle
        private GLControl mViewerCtrl;

        // model render data
        private List<GpuModel> mModels;
        private Dictionary<string, int> mTexLookup;
        private ShaderProgram mShaderProg;
        private int mCurrentGpuModelId;
        private int mCurrentTextureId;
        private Matrix4 mTransform;

        // camera data
        private Camera mCamera;
        private Vector3 mTp;

        // freecam mouse look
        private bool  mRightMouseDown = false;
        private Point mLastMousePos;
        private float mMoveSpeed = 200f;

        private System.Diagnostics.Stopwatch mFrameTimer = System.Diagnostics.Stopwatch.StartNew();

        // clear color
        private Color mBgColor;

        public Color BgColor
        {
            get { return mBgColor; }
            set
            {
                mBgColor = value;
                GL.ClearColor(mBgColor);
                mViewerCtrl.Invalidate();
            }
        }

        public static bool IsSupported
        {
            get
            {
                var version = GL.GetString( StringName.Version );
                return version[0] >= '3' && version[2] >= '3'; // X.Y
            }
        }

        public ModelViewer(GLControl controller)
        {
            mCanRender = false;

            if ( !InitializeShaderProgram() )
                return;

            mCanRender = true;

            string version = GL.GetString(StringName.Version);
            Console.WriteLine( version );

            mIsViewReady = false;
            mViewerCtrl = controller;
            mModels = new List<GpuModel>();
            mTexLookup = new Dictionary<string, int>();

            GL.ClearColor(new Color4(128, 128, 128, 255));
            GL.FrontFace(FrontFaceDirection.Ccw);
            GL.CullFace(CullFaceMode.Back);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            mViewerCtrl.Paint += ModelViewerPaint;
            mViewerCtrl.Resize += ModelViewerResize;
            mViewerCtrl.Enter += (s, e) => { mIsViewFocused = true; };
            mViewerCtrl.Leave += (s, e) => { mIsViewFocused = false; };
            mViewerCtrl.KeyDown  += OnKeyDown;
            mViewerCtrl.MouseDown += OnMouseDown;
            mViewerCtrl.MouseUp   += OnMouseUp;
            mViewerCtrl.MouseMove += OnMouseMove;
            mViewerCtrl.MouseWheel += OnMouseWheel;

            mTp = new Vector3();
            mIsViewReady = true;
            GL.Viewport(0, 0, controller.Width, controller.Height);
            mCamera = new Camera();

            Application.Idle += OnApplicationIdle;
        }

        private bool InitializeShaderProgram()
        {
            try
            {
                // set up shader program
                mShaderProg = new ShaderProgram( "shader" );
                mShaderProg.AddUniform( "proj" );
                mShaderProg.AddUniform( "view" );
                mShaderProg.AddUniform( "tran" );
                mShaderProg.AddUniform( "diffuse" );
                mShaderProg.AddUniform( "diffuseColor" );
                mShaderProg.AddUniform( "isTextured" );
                mShaderProg.Bind();
            }
            catch ( Exception )
            {
                return false;
            }

            return true;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PeekMessage(out NativeMsg msg, IntPtr hWnd,
            uint filterMin, uint filterMax, uint removeMsg);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeMsg
        {
            public IntPtr hWnd, wParam, lParam;
            public uint message, time;
            public System.Drawing.Point pt;
        }

        private static bool IsMessageQueueEmpty()
            => !PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            while (mIsViewReady && mCanRender && mViewerCtrl.IsHandleCreated && IsMessageQueueEmpty())
            {
                float dt = (float)mFrameTimer.Elapsed.TotalSeconds;
                mFrameTimer.Restart();
                if (dt > 0.1f) dt = 0.1f; // clamp so a hitch can't teleport the camera

                UpdateCamera(dt);

                ModelViewerPaint(this, null);
            }
        }

        private void UpdateCamera(float dt)
        {
            if (!mIsViewFocused) return;

            var kb = OpenTK.Input.Keyboard.GetState();
            Vector3 oldPos = mCamera.Position;

            float speed = mMoveSpeed * dt;
            if (kb[Key.ShiftLeft] || kb[Key.ShiftRight]) speed *= 3f;

            if (kb[Key.W]) mCamera.Position += mCamera.Forward * speed;
            if (kb[Key.S]) mCamera.Position -= mCamera.Forward * speed;
            if (kb[Key.D]) mCamera.Position += mCamera.Right   * speed;
            if (kb[Key.A]) mCamera.Position -= mCamera.Right   * speed;
            if (kb[Key.E]) mCamera.Position += mCamera.Up      * speed;
            if (kb[Key.Q]) mCamera.Position -= mCamera.Up      * speed;

            if (IsNaN(mCamera.Position))
                mCamera.Position = IsNaN(oldPos) ? Vector3.Zero : oldPos;
        }

        private void OnKeyDown(object sender, KeyEventArgs args) { /* movement polled in idle loop */ }

        private void OnMouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mRightMouseDown = true;
                mLastMousePos   = e.Location;
                mViewerCtrl.Capture = true;   // keep receiving events outside the control
            }
        }

        private void OnMouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mRightMouseDown = false;
                mViewerCtrl.Capture = false;
            }
        }

        private void OnMouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (!mRightMouseDown || !mCanRender)
                return;

            float dx = e.X - mLastMousePos.X;
            float dy = e.Y - mLastMousePos.Y;
            mLastMousePos = e.Location;

            mCamera.Rotate(dx, dy);
            mViewerCtrl.Invalidate();
        }

        private void OnMouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (!mCanRender)
                return;

            // each notch = 120 units; scale speed up/down by 20 %
            float factor = e.Delta > 0 ? 1.2f : 1f / 1.2f;
            mMoveSpeed = Math.Max(1f, mMoveSpeed * factor);
        }

        public static bool IsNaN(Vector3 value)
        {
            return float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z);
        }

        private static byte ScalePS2Alpha(byte a) => (byte)Math.Min((a / 128.0f) * 255, 255);

        private bool ModelHasTransparency(GpuModel model)
        {
            for (int i = 0; i < model.Color.Length; i++)
            {
                if (ScalePS2Alpha(model.Color[i].A) < 255)
                    return true;

                if (model.Tid[i] != string.Empty && mTransparentTextures.Contains(model.Tid[i]))
                    return true;
            }
            return false;
        }

        private HashSet<string> mTransparentTextures = new HashSet<string>();

        public void LoadTextures(RwTextureDictionaryNode textures)
        {
            if (textures == null || !mCanRender )
                return;

            int textureIdx = 0;
            foreach ( RwTextureNativeNode texture in textures.Textures )
            {
                Console.WriteLine( "processing texture: {0}", textureIdx++ );

                var colorpixels = texture.GetPixels();

                // get the pixel array
                var pixels = new BasicCol4[texture.Width * texture.Height];
                for ( int i = 0; i < texture.Width * texture.Height; i++ )
                {
                    var c = colorpixels[i];
                    pixels[i] = new BasicCol4( Color.FromArgb(
                        ScalePS2Alpha( c.A ),
                        c.R, c.G, c.B ) );
                }

                // flag texture as transparent if any pixel is non-opaque
                bool hasAlpha = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].A < 255) { hasAlpha = true; break; }
                }
                if (hasAlpha)
                    mTransparentTextures.Add(texture.Name);

                // create the texture
                int tex = GL.GenTexture();
                GL.BindTexture( TextureTarget.Texture2D, tex );

                // GL 4.5
                //GL.CreateTextures( TextureTarget.Texture2D, 1, out int tex );

                // set up params
                GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapS, RwtoGlConversionHelper.WrapDictionary[texture.HorrizontalAddressingMode] );
                GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapT, RwtoGlConversionHelper.WrapDictionary[texture.VerticalAddressingMode] );
                GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, RwtoGlConversionHelper.FilterDictionary[texture.FilterMode] );
                GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, RwtoGlConversionHelper.FilterDictionary[texture.FilterMode] );

                // GL 4.5
                //GL.TextureParameter( tex, TextureParameterName.TextureWrapS, RwtoGlConversionHelper.WrapDictionary[texture.HorrizontalAddressingMode] );
                //GL.TextureParameter( tex, TextureParameterName.TextureWrapT, RwtoGlConversionHelper.WrapDictionary[texture.VerticalAddressingMode] );
                //GL.TextureParameter( tex, TextureParameterName.TextureMagFilter, RwtoGlConversionHelper.FilterDictionary[texture.FilterMode] );
                //GL.TextureParameter( tex, TextureParameterName.TextureMinFilter, RwtoGlConversionHelper.FilterDictionary[texture.FilterMode] );

                // set up the bitmap data
                GL.TexImage2D( TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels );

                // GL 4.5
                //GL.TextureStorage2D( tex, 1, SizedInternalFormat.Rgba8, texture.Width, texture.Height );
                //GL.TextureSubImage2D( tex, 0, 0, 0, texture.Width, texture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels );

                // add a texture lookup for the processed texture
                mTexLookup.Add( texture.Name, tex );
            }
        }

        public void LoadModel(RwClumpNode clump)
        {
            if ( !mCanRender )
                return;

            Console.WriteLine( "geometry count: {0}", clump.GeometryCount );

            for ( var atomicIndex = 0; atomicIndex < clump.Atomics.Count; atomicIndex++ )
            {
                RwAtomicNode atomic = clump.Atomics[atomicIndex];
                Console.WriteLine( "processing draw call: {0}", atomicIndex );
                Console.WriteLine( "geo:{0}\t frame:{1}\t flag1:{2}\t flag2{3}\t", atomic.GeometryIndex, atomic.FrameIndex, atomic.Flag1, atomic.Flag2 );

                var geom = clump.GeometryList[atomic.GeometryIndex];
                var frame = clump.FrameList[atomic.FrameIndex];

                LoadGeometry(geom, frame.WorldTransform);
            }
        }

        public void LoadGeometry(RwGeometryNode geom, SN.Matrix4x4 transform)
        {
            if ( !mCanRender )
                return;

            if (geom.MeshListNode == null)
            {
                geom.MeshListNode = new RwMeshListNode(geom);
            }

            Console.WriteLine( geom.MeshListNode.MeshCount );
            Console.WriteLine( geom.MeshListNode.PrimitiveType );

            Vertex[] vertices = new Vertex[geom.VertexCount];
            int[][] allIndices = new int[geom.MeshListNode.MeshCount][];
            int[] allIndicesCount = new int[geom.MeshListNode.MeshCount];
            int[] fullIndices = new int[geom.TriangleCount * 3];

            for ( int i = 0; i < geom.TriangleCount; i++ )
            {
                fullIndices[i * 3 + 0] = geom.Triangles[i].A;
                fullIndices[i * 3 + 1] = geom.Triangles[i].B;
                fullIndices[i * 3 + 2] = geom.Triangles[i].C;
            }

            // remap the vertices
            for ( int i = 0; i < geom.VertexCount; i++ )
            {
                // set the new interleaved vertex
                Vertex vtx = new Vertex
                {
                    Pos = new BasicVec3( SN.Vector3.Transform( geom.Vertices[i], transform ) )
                };

                if ( geom.HasNormals )
                    vtx.Nrm = new BasicVec3( SN.Vector3.TransformNormal( geom.Normals[i], transform ) );

                if ( geom.TextureCoordinateChannelCount > 0 )
                    vtx.Tex = new BasicVec2( geom.TextureCoordinateChannels[0][i] );

                if ( geom.HasColors )
                    vtx.Col = new BasicVec4( geom.Colors[i] );
                else
                    vtx.Col = new BasicVec4( 1, 1, 1, 1 );

                vertices[i] = vtx;
            }

            for ( int i = 0; i < geom.MeshListNode.MeshCount; i++ )
            {
                allIndices[i] = geom.MeshListNode.MaterialMeshes[i].Indices;
                allIndicesCount[i] = geom.MeshListNode.MaterialMeshes[i].IndexCount;
            }

            Console.WriteLine( "tex channels: " + geom.TextureCoordinateChannelCount );
            Console.WriteLine( "num materials: " + geom.MaterialCount );

            for ( int m = 0; m < geom.MaterialCount; m++ )
                Console.WriteLine( "material: {0}", geom.Materials[m] );

            // setup the vbo
            int vbo = GL.GenBuffer();
            GL.BindBuffer( BufferTarget.ArrayBuffer, vbo );
            GL.BufferData( BufferTarget.ArrayBuffer, sizeof( float ) * 12 * vertices.Length, vertices, BufferUsageHint.StaticDraw );

            // setup the ibo
            int[] ibo = new int[allIndices.Length];
            GL.GenBuffers( allIndices.Length, ibo );
            for ( int i = 0; i < ibo.Length; i++ )
            {
                GL.BindBuffer( BufferTarget.ElementArrayBuffer, ibo[i] );
                GL.BufferData( BufferTarget.ElementArrayBuffer, sizeof( int ) * allIndices[i].Length, allIndices[i], BufferUsageHint.StaticDraw );
            }

            // setup the vertex attribs
            GL.VertexAttribPointer( 0, 3, VertexAttribPointerType.Float, false, sizeof( float ) * 12, 0 );
            GL.VertexAttribPointer( 1, 3, VertexAttribPointerType.Float, false, sizeof( float ) * 12, ( sizeof( float ) * 3 ) );
            GL.VertexAttribPointer( 2, 2, VertexAttribPointerType.Float, false, sizeof( float ) * 12, ( sizeof( float ) * 6 ) );
            GL.VertexAttribPointer( 3, 4, VertexAttribPointerType.Float, false, sizeof( float ) * 12, ( sizeof( float ) * 8 ) );

            // get the mesh color from the material
            // get the texture name if there's a texture assigned
            string[] texname = new string[geom.MeshListNode.MeshCount];
            Color[] colors = new Color[geom.MeshListNode.MeshCount];
            for ( int i = 0; i < texname.Length; i++ )
            {
                texname[i] = geom.MaterialCount > 0 && geom.Materials[geom.MeshListNode.MaterialMeshes[i].MaterialIndex].IsTextured ?
                        geom.Materials[geom.MeshListNode.MaterialMeshes[i].MaterialIndex].TextureReferenceNode.Name
                        : string.Empty;

                colors[i] = geom.MaterialCount > 0
                    ? geom.Materials[geom.MeshListNode.MaterialMeshes[i].MaterialIndex].Color
                    : Color.White;
            }

            // add the render model to the list
            mModels.Add( new GpuModel( vbo, ibo, geom.VertexCount, allIndicesCount, texname, colors,
                geom.MeshListNode.PrimitiveType == RwPrimitiveType.TriangleStrip ) );
        }

        public void LoadScene(RmdScene rmdScene)
        {
            if ( !mCanRender )
                return;

            Console.WriteLine("loading scene");
            Console.WriteLine("num textures: {0} ", rmdScene.TextureDictionary != null ? rmdScene.TextureDictionary.TextureCount : 0);

            LoadTextures(rmdScene.TextureDictionary);

            foreach (var clump in rmdScene.Clumps)
            {
                LoadModel( clump );
            }
        }

        public void DeleteScene()
        {
            if ( !mCanRender )
                return;

            Console.WriteLine("deleting scene");
            EndBind();
            Console.WriteLine("unbind models");
            GL.BindTexture(TextureTarget.Texture2D, 0);
            Console.WriteLine("unbind textures");
            mCurrentTextureId = mCurrentGpuModelId = 0;

            // death to the textures
            int[] texIds = new int[mTexLookup.Count];
            mTexLookup.Values.CopyTo(texIds, 0);
            for (int i = 0; i < mTexLookup.Count; i++)
            {
                Console.WriteLine("deleting texture: " + i);
                GL.DeleteTexture(texIds[i]);
            }

            // death to models
            for (int i = 0; i < mModels.Count; i++)
            {
                Console.WriteLine("deleting model: " + i);

                if(mModels[i].Vbo != 0)
                    GL.DeleteBuffer(mModels[i].Vbo);

                if(mModels[i].Ibo[0] != 0)
                    GL.DeleteBuffers(mModels[0].Ibo.Length, mModels[i].Ibo);
            }

            mModels.Clear();
            mTexLookup.Clear();
            mTransparentTextures.Clear();
        }

        public void DisposeViewer()
        {
            Console.WriteLine("disposing");
            Application.Idle -= OnApplicationIdle;
            DeleteScene();
            mShaderProg.Delete();
            mIsViewReady = false;
        }

        private void BeginBind(GpuModel data)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, data.Vbo);
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 12, 0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, sizeof(float) * 12, sizeof(float) * 3);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, sizeof(float) * 12, sizeof(float) * 6);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, sizeof(float) * 12, sizeof(float) * 8);
        }

        private void SubBind(GpuModel data)
        {
            for (int i = 0; i < data.Ibo.Length; i++)
            {
                int isTextured = 0;

                if (data.Tid[i] != string.Empty)
                {
                    bool texPresent = mTexLookup.TryGetValue( data.Tid[i], out int texIdx );

                    if (texPresent)
                    {
                        isTextured = 1;
                        FullTexBind(texIdx);
                    }
                }

                // set shader vars
                mShaderProg.SetUniform("diffuseColor", data.Color[i]);
                mShaderProg.SetUniform("isTextured", isTextured);

                // bind model
                GL.BindBuffer(BufferTarget.ArrayBuffer, data.Vbo);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, data.Ibo[i]);
                GL.DrawElements(data.Strip ? BeginMode.TriangleStrip : BeginMode.Triangles, data.Ic[i], DrawElementsType.UnsignedInt, 0);
            }
        }

        private void EndBind()
        {
            GL.DisableVertexAttribArray(0);
            GL.DisableVertexAttribArray(1);
            GL.DisableVertexAttribArray(2);
            GL.DisableVertexAttribArray(3);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        }

        private void FullBind(GpuModel data) // for optimization
        {
            if (data.Vbo == 0)
                return;

            if (mCurrentGpuModelId != data.Vbo)
            {
                EndBind();  // incase the previous was a SubBind
                BeginBind(data);
                SubBind(data);
                mCurrentGpuModelId = data.Vbo;
            }
            else
            {
                SubBind(data);
            }
        }

        private void FullTexBind(int texture)
        {
            if (texture == 0 || texture == mCurrentTextureId)
                return;

            GL.BindTexture(TextureTarget.Texture2D, texture);
            mCurrentTextureId = texture;
        }

        private void ModelViewerResize(object sender, EventArgs e)
        {
            if (!mIsViewReady || !mCanRender )
                return;

            GL.Viewport(0, 0, mViewerCtrl.Width, mViewerCtrl.Height);
        }

        private void ModelViewerPaint(object sender, PaintEventArgs e)
        {
            if (!mIsViewReady || !mViewerCtrl.Visible || !mCanRender || !mViewerCtrl.IsHandleCreated)
                return;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            mShaderProg.Bind();
            mCamera.Bind( mShaderProg, mViewerCtrl );
            mTransform = Matrix4.CreateTranslation( mTp ) * Matrix4.CreateFromQuaternion( Quaternion.FromEulerAngles( 0, 0, 0 ) ) * Matrix4.CreateScale( new Vector3( 1.0f, 1.0f, 1.0f ) );
            mShaderProg.SetUniform( "tran", mTransform );

            if (mModels.Count > 0)
            {
                GL.DepthMask(true);
                for (int i = 0; i < mModels.Count; i++)
                {
                    if (!ModelHasTransparency(mModels[i]))
                        FullBind(mModels[i]);
                }

                GL.DepthMask(false);
                for (int i = 0; i < mModels.Count; i++)
                {
                    if (ModelHasTransparency(mModels[i]))
                        FullBind(mModels[i]);
                }

                GL.DepthMask(true);
            }

            mViewerCtrl.SwapBuffers();
        }
    }
}
