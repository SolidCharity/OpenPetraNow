/*************************************************************************
 *
 * DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
 *
 * @Authors:
 *       christiank
 *
 * Copyright 2004-2009 by OM International
 *
 * This file is part of OpenPetra.org.
 *
 * OpenPetra.org is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * OpenPetra.org is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.
 *
 ************************************************************************/
using System;
using System.IO;

using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace Ict.Common.IO
{
    /// <summary>
    /// Helps in handling with certain aspects of files and compression.
    /// </summary>
    public static class TFileHelper
    {
        /// <summary>
        /// Helps in handling Streams and files.
        /// </summary>
        public static class Streams
        {
            /// <summary>
            /// Helps in handling Zip-compression of Streams and files.
            /// </summary>
            public static class Compression
            {
                /// <summary>
                /// Loads a file, Zip-compresses it in memory and returns it as a MemoryStream.
                /// </summary>
                /// <param name="AFilePath">Filename (including Path) to read from.</param>
                /// <returns>A MemoryStream with the Zip-compressed contents of the file specified in <paramref name="AFilePath" />.</returns>
                public static MemoryStream InflateFileIntoMemoryStream(string AFilePath)
                {
                    return InflateFilesIntoMemoryStream(new string[] { AFilePath });
                }

                /// <summary>
                /// Loads a any number of files, Zip-compresses them in memory into one Zip archive and returns it as a MemoryStream.
                /// </summary>
                /// <param name="AFilePaths">Array of Filenames (including Paths) to read from.</param>
                /// <returns>A MemoryStream with the Zip-compressed contents of all the files specified in <paramref name="AFilePaths" />.</returns>
                public static MemoryStream InflateFilesIntoMemoryStream(string[] AFilePaths)
                {
                    MemoryStream ZippedStream = new MemoryStream();
                    MemoryStream OutputStream = new MemoryStream();
                    ZipEntry ZippedFile;

                    byte[] buffer = new byte[4096];


                    using (ZipOutputStream ZipStream = new ZipOutputStream(ZippedStream))
                    {
                        ZipStream.SetLevel(9);       // 0 - store only to 9 - means best compression

                        foreach (string FileToBeZipped in AFilePaths)
                        {
                            ZippedFile = new ZipEntry(FileToBeZipped);
                            ZipStream.PutNextEntry(ZippedFile);

                            using (FileStream fs = File.OpenRead(FileToBeZipped)) {
                                StreamUtils.Copy(fs, ZipStream, buffer);
                            }

//MessageBox.Show("1:" + ZippedStream.Length.ToString());
                        }

                        ZipStream.Finish();

//MessageBox.Show("2:" + ZippedStream.Length.ToString());
//                          ZipStream.Close();
                        ZippedStream.WriteTo(OutputStream);
                    }

                    // Ensure that the user of OutputStream is reading from the beginning...
                    OutputStream.Seek(0, SeekOrigin.Begin);

//MessageBox.Show("3:" + OutputStream.Length.ToString());
                    return OutputStream;
                }

                /// <summary>
                /// Uncompress a Zip-compressed Stream into a MemoryStream.
                /// </summary>
                /// <param name="AZippedStream">Stream containing files that are Zip-compressed.</param>
                /// <returns>A MemoryStream with the uncompressed contents of the Stream specified in <paramref name="AZippedStream" />.</returns>
                public static MemoryStream DeflateFilesFromStream(Stream AZippedStream)
                {
                    MemoryStream UnzippedStream = new MemoryStream();
                    ZipEntry ZippedFile;
                    Int32 size = 0;

                    // Always ensure we are reading from the beginning...
                    AZippedStream.Seek(0, SeekOrigin.Begin);

                    using (ZipInputStream s = new ZipInputStream(AZippedStream))
                    {
                        while ((ZippedFile = s.GetNextEntry()) != null)
                        {
                            Byte[] buffer = new Byte[4096];

                            while (true)
                            {
                                size = s.Read(buffer, 0, buffer.Length);

                                if (size > 0)
                                {
                                    UnzippedStream.Write(buffer, 0, size);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }

                    return UnzippedStream;
                }
            }

            /// <summary>
            /// Helps in handling MemoryStreams and files.
            /// </summary>
            public static class FileHandling
            {
                /// <summary>
                /// Loads a file into a MemoryStream object.
                /// </summary>
                /// <param name="AFilePath">Filename (including Path) to read from.</param>
                /// <returns>A MemoryStream with the contents of the File specified in <paramref name="AFilePath" />.</returns>
                public static MemoryStream LoadFileIntoStream(string AFilePath)
                {
                    int blockSize = 1024;
                    int bytesNum;

                    byte[] buffer = new byte[blockSize];

                    MemoryStream OutputStream = new MemoryStream();

                    using (FileStream fs = new FileStream(AFilePath, FileMode.Open, FileAccess.Read))
                    {
                        while ((bytesNum = fs.Read(buffer, 0, blockSize)) > 0)
                        {
                            OutputStream.Write(buffer, 0, bytesNum);
                        }
                    }

                    // Ensure that the user of OutputStream is reading from the beginning...
                    OutputStream.Seek(0, SeekOrigin.Begin);

                    return OutputStream;
                }

                /// <summary>
                /// Saves a Stream into a file.
                /// </summary>
                /// <param name="AStream">The Stream whose contens should be saved to the file.</param>
                /// <param name="AFilePath">Filename (including Path) to write to.</param>
                /// <returns>void</returns>
                public static void SaveStreamToFile(Stream AStream, string AFilePath)
                {
                    Int32 size = 0;

//MessageBox.Show("4:" + AStream.Length.ToString());

                    // Always ensure we are reading from the beginning...
                    AStream.Seek(0, SeekOrigin.Begin);

                    using (FileStream Writer = System.IO.File.OpenWrite(AFilePath))
                    {
                        Byte[] buffer = new Byte[4096];

                        while (true)
                        {
                            size = AStream.Read(buffer, 0, buffer.Length);

//MessageBox.Show("5:" + size.ToString());
                            if (size > 0)
                            {
                                Writer.Write(buffer, 0, size);
                            }
                            else
                            {
                                break;
                            }
                        }

                        Writer.Flush();
                    }

                    // Ensure that the user of OutputStream is reading from the beginning...
                    AStream.Seek(0, SeekOrigin.Begin);
                }
            }
        }
    }
}