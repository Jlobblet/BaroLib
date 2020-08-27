using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace BaroLib
{
    public class FormUtil
    {
        public static string ShowFolderBrowserDialog()
        {
            using var dialog = new CommonOpenFileDialog
                               {
                                   IsFolderPicker = true,
                                   EnsureFileExists = true,
                                   EnsurePathExists = true,
                               };
            return dialog.ShowDialog() == CommonFileDialogResult.Ok ? dialog.FileName : "";
        }

        public static string ShowFolderBrowserDialog(string defaultDirectory)
        {
            using var dialog = new CommonOpenFileDialog
                               {
                                   IsFolderPicker = true,
                                   EnsureFileExists = true,
                                   EnsurePathExists = true,
                                   DefaultDirectory = defaultDirectory,
                               };
            return dialog.ShowDialog() == CommonFileDialogResult.Ok ? dialog.FileName : "";
        }

        public static string ShowFileBrowserDialog(string extension = "")
        {
            using var dialog = new CommonOpenFileDialog
                               {
                                   EnsureFileExists = true,
                                   EnsurePathExists = true,
                               };
            dialog.FileOk += (sender, parameter) =>
                             {
                                 var commonOpenFileDialog = (CommonOpenFileDialog)sender;
                                 var filenames = new Collection<string>();
                                 typeof(CommonOpenFileDialog)
                                     .GetMethod("PopulateWithFileNames", BindingFlags.Instance | BindingFlags.NonPublic)
                                     ?.Invoke(commonOpenFileDialog, new object[] {filenames});
                                 string filename = filenames[0];
                                 if (extension == "" || Path.GetExtension(filename) == extension)
                                 {
                                     return;
                                 }

                                 parameter.Cancel = true;
                                 MessageBox.Show($"The selected file does not have the extension {extension}.", "Error",
                                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
                             };
            return dialog.ShowDialog() == CommonFileDialogResult.Ok ? dialog.FileName : "";
        }

        public static Image GetImageFromString(string s)
        {
            byte[] bytes = Convert.FromBase64String(s);
            using var ms = new MemoryStream(bytes);
            return Image.FromStream(ms);
        }

        public class ControlWriter : TextWriter
        {
            private readonly Control textbox;

            public ControlWriter(Control textbox) => this.textbox = textbox;

            public override Encoding Encoding => Encoding.ASCII;

            public override void Write(char value)
            {
                textbox.Text += value;
            }

            public override void Write(string value)
            {
                textbox.Text += value;
            }
        }

        public class MultiTextWriter : TextWriter
        {
            private readonly IEnumerable<TextWriter> writers;

            public MultiTextWriter(IEnumerable<TextWriter> writers) => this.writers = writers.ToList();

            public MultiTextWriter(params TextWriter[] writers) => this.writers = writers;

            public override Encoding Encoding => Encoding.ASCII;

            public override void Write(char value)
            {
                foreach (TextWriter writer in writers)
                {
                    writer.Write(value);
                }
            }

            public override void Write(string value)
            {
                foreach (TextWriter writer in writers)
                {
                    writer.Write(value);
                }
            }

            public override void Flush()
            {
                foreach (TextWriter writer in writers)
                {
                    writer.Flush();
                }
            }

            public override void Close()
            {
                foreach (TextWriter writer in writers)
                {
                    writer.Close();
                }
            }
        }
    }
}
