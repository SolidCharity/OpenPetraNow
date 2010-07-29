//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       <please insert your name>
//
// Copyright 2004-2010 by OM International
//
// This file is part of OpenPetra.org.
//
// OpenPetra.org is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// OpenPetra.org is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.
//
using System;
using System.Data;
using System.Configuration;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using System.IO;

namespace {#NAMESPACE}
{
    /// This is a test for the parameter list which is used for reporting.
    [TestFixture]
    public class TMyTest
    {
        /// <summary>
        /// open database connection or prepare other things for this test
        /// </summary>
        [SetUp]
        public void Init()
        {
        }

        /// <summary>
        /// cleaning up everything that was set up for this test
        /// </summary>
        [TearDown]
        public void TearDown()
        {
        }

        /// <summary>
        /// Some test, please add comment
        /// </summary>
        [Test]
        public void TestTemplate()
        {
            // TODO please insert your test codes here
            // Assert.AreEqual("0", "1", "this will always fail");
        }
    }
}