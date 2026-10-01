using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
namespace GameAuthoringLab;
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UiModel { public uint Size,Generation;public int Volume;public uint Reserved;public fixed byte Name[128];public fixed byte Status[256]; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UiAction { public uint Size,Generation,Action;public int Volume;public fixed byte Name[128]; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UiState { public uint Size,Generation,Loaded,Pending,Queued,Overflow,KeyboardFocus;public float ScrollTop;public fixed byte Diagnostic[512]; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UiTextState
{
 public uint Size,Version,Generation,Flags;
 public int SelectionStart,SelectionEnd,AreaX,AreaY,AreaWidth,AreaHeight;
 public float CaretX,CaretY,LineHeight;
 public uint PreeditScalars,Failures,Reserved;
 public fixed byte Value[512]; public fixed byte Diagnostic[256];
 public readonly bool Composing => (Flags&2)!=0;
 public readonly bool TextActive => (Flags&4)!=0;
}
internal static unsafe partial class UiNative
{
 [LibraryImport("gal",EntryPoint="gal_ui_open",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Open(nint c,string path,string font);
 [LibraryImport("gal",EntryPoint="gal_ui_close")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Close(nint c);
 [LibraryImport("gal",EntryPoint="gal_ui_set_model")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int SetModel(nint c,UiModel* model);
 [LibraryImport("gal",EntryPoint="gal_ui_poll_action")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Poll(nint c,UiAction* action);
 [LibraryImport("gal",EntryPoint="gal_ui_get_state")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int State(nint c,UiState* state);
 [LibraryImport("gal",EntryPoint="gal_ui_get_text_state")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int TextState(nint c,UiTextState* state);
 [LibraryImport("gal",EntryPoint="gal_ui_test_command")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Command(nint c,uint generation,uint command);
 [LibraryImport("gal",EntryPoint="gal_capture_next",StringMarshalling=StringMarshalling.Utf8)] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial int Capture(nint c,string path);
 internal static void Put(byte* target,int size,string value){var bytes=new UTF8Encoding(false,true).GetBytes(value);if(bytes.Length>=size)throw new ArgumentException("UI UTF8 field exceeds capacity.");new Span<byte>(target,size).Clear();bytes.CopyTo(new Span<byte>(target,size));}
 internal static string Text(byte* value,int size){var span=new ReadOnlySpan<byte>(value,size);int end=span.IndexOf((byte)0);if(end<0)throw new InvalidDataException("Unterminated UI ABI text");return new UTF8Encoding(false,true).GetString(span[..end]);}
}
