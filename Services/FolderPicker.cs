using System;
using System.Runtime.InteropServices;

namespace dlss_updater.Services
{
    internal static class FolderPicker
    {
        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(
                uint cFileTypes,
                [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] filters);

            void SetFileTypeIndex(uint iFileType);

            uint GetFileTypeIndex();

            void Advise(IntPtr pfde, out uint cookie);

            void Unadvise(uint cookie);

            void SetOptions(uint fos);

            uint GetOptions();

            void SetDefaultFolder(IShellItem psi);

            void SetFolder(IShellItem psi);

            void GetFolder(out IShellItem ppsi);

            void GetCurrentSelection(out IShellItem ppsi);

            void SetFileName(
                [MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetFileName(
                [MarshalAs(UnmanagedType.LPWStr)] out string name);

            void SetTitle(
                [MarshalAs(UnmanagedType.LPWStr)] string title);

            void SetOkButtonLabel(
                [MarshalAs(UnmanagedType.LPWStr)] string text);

            void SetFileNameLabel(
                [MarshalAs(UnmanagedType.LPWStr)] string text);

            void GetResult(out IShellItem ppsi);

            void AddPlace(IShellItem psi, uint fdap);

            void Close(int hr);

            void SetDefaultExtension(
                [MarshalAs(UnmanagedType.LPWStr)] string extension);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(
                IntPtr pbc,
                ref Guid bhid,
                ref Guid riid,
                out IntPtr ppv);

            void GetParent(out IShellItem ppsi);

            void GetDisplayName(
                SIGDN sigdnName,
                out IntPtr ppszName);

            void GetAttributes(
                uint sfgaoMask,
                out uint psfgaoAttribs);

            void Compare(
                IShellItem psi,
                uint hint,
                out int piOrder);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszSpec;
        }

        private enum SIGDN : uint
        {
            FILESYSPATH = 0x80058000
        }

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;

        private const int S_OK = 0;

        private static readonly Guid CLSID_FileOpenDialog =
            new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

        public static string PickFolder(IntPtr owner)
        {
            object dialog = null;

            try
            {
                Type dialogType =
                    Type.GetTypeFromCLSID(
                        CLSID_FileOpenDialog);

                dialog =
                    Activator.CreateInstance(dialogType);

                IFileDialog fileDialog =
                    (IFileDialog)dialog;

                fileDialog.SetOptions(
                    FOS_PICKFOLDERS |
                    FOS_FORCEFILESYSTEM |
                    FOS_PATHMUSTEXIST);

                int result =
                    fileDialog.Show(owner);

                if (result != S_OK)
                {
                    return null;
                }

                IShellItem item;

                fileDialog.GetResult(
                    out item);

                IntPtr pathPtr;

                item.GetDisplayName(
                    SIGDN.FILESYSPATH,
                    out pathPtr);

                try
                {
                    return Marshal.PtrToStringUni(
                        pathPtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(
                        pathPtr);
                }
            }
            finally
            {
                if (dialog != null)
                {
                    Marshal.ReleaseComObject(
                        dialog);
                }
            }
        }
    }
}