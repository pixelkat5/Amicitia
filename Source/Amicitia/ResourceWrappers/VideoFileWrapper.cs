using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AmicitiaLibrary.IO;

namespace Amicitia.ResourceWrappers
{
    public class VideoFileWrapper : ResourceWrapper<BinaryFile>
    {
        public VideoFileWrapper( string text, BinaryFile resource ) : base( text, resource )
        {
        }

        protected override void Initialize()
        {
            CommonContextMenuOptions = CommonContextMenuOptions.Export | CommonContextMenuOptions.Replace |
                                       CommonContextMenuOptions.Move | CommonContextMenuOptions.Rename | CommonContextMenuOptions.Delete;

            RegisterFileExportAction( SupportedFileType.Resource, ( res, path ) => res.Save( path ) );
            RegisterFileExportAction( SupportedFileType.Mp4File, ( res, path ) => ExportWithFFmpeg( res, path ) );
            RegisterFileReplaceAction( SupportedFileType.Resource, ( res, path ) => new BinaryFile( path ) );
        }

        private void ExportWithFFmpeg( BinaryFile res, string outputPath )
        {
            var ffmpeg = FindFFmpeg();
            if ( ffmpeg == null )
            {
                MessageBox.Show(
                    "ffmpeg.exe not found. Place ffmpeg.exe in the same folder as Amicitia.exe to enable video export.",
                    "FFmpeg not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning );
                return;
            }

            var inputExt = Path.GetExtension( Text );
            var tempInput = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() + inputExt );

            try
            {
                res.Save( tempInput );

                var errorLog = new StringBuilder();
                var args = $"-y -i \"{tempInput}\" -c:v libx264 -c:a aac \"{outputPath}\"";
                var psi = new ProcessStartInfo( ffmpeg, args )
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };

                using ( var process = Process.Start( psi ) )
                {
                    process.ErrorDataReceived += ( s, e ) => { if ( e.Data != null ) errorLog.AppendLine( e.Data ); };
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if ( process.ExitCode != 0 )
                    {
                        MessageBox.Show( $"FFmpeg failed:\n{errorLog}", "Export error", MessageBoxButtons.OK, MessageBoxIcon.Error );
                    }
                }
            }
            finally
            {
                if ( File.Exists( tempInput ) )
                    File.Delete( tempInput );
            }
        }

        private static string FindFFmpeg()
        {
            var exeDir = Path.GetDirectoryName( System.Reflection.Assembly.GetExecutingAssembly().Location );
            var local = Path.Combine( exeDir, "ffmpeg.exe" );

            if ( File.Exists( local ) )
                return local;

            foreach ( var dir in ( System.Environment.GetEnvironmentVariable( "PATH" ) ?? "" ).Split( Path.PathSeparator ) )
            {
                var candidate = Path.Combine( dir.Trim(), "ffmpeg.exe" );
                if ( File.Exists( candidate ) )
                    return candidate;
            }

            return null;
        }

        protected override void PopulateView()
        {
        }
    }
}
