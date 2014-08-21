﻿// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       Tim Ingham
//
// Copyright 2004-2014 by OM International
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
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;
using System.Resources;
using System.Collections.Specialized;

using Ict.Common;
using Ict.Common.Verification;
using Ict.Common.Controls;
using Ict.Petra.Client.App.Core;
using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.App.Gui;
using Ict.Petra.Client.CommonControls;
using Ict.Petra.Client.CommonForms;
using Ict.Petra.Shared;
using GNU.Gettext;
using Ict.Petra.Shared.MFinance.GL.Data;
using Ict.Petra.Shared.MFinance.Account.Data;

namespace Ict.Petra.Client.MFinance.Gui.Setup
{
    public partial class TUC_CostCentreList
    {
        private TFrmGLCostCentreHierarchy FParentForm = null;

        private bool FIsFilterPanelInitialised = false;
        private TextBox FFilterTxtCostCentreCode = null;
        private TCmbAutoComplete FFilterCmbCostCentreType = null;
        private TextBox FFilterTxtCostCentreName = null;
        private CheckBox FFilterChkActive = null;

        private TSgrdDataGridPaged grdDetails = null;
        private int FPrevRowChangedRow = -1;
        private DataRow FPreviouslySelectedDetailRow = null;

        // The CostCentre selected in the parent form
        CostCentreNodeDetails FSelectedCostCentre;
//      Int32 FLedgerNumber;
        DataView FDataView = null;

        /// <summary>
        /// I don't want this, but the auto-generated code references it:
        /// </summary>
        public GLSetupTDS MainDS;

        /// <summary>
        /// The CostCentre may have been selected in the tree view, and copied here.
        /// </summary>
        public CostCentreNodeDetails SelectedCostCentre
        {
            set
            {
                FSelectedCostCentre = value;

                if (FDataView != null)
                {
                    Int32 RowIdx = -1;

                    if (FSelectedCostCentre != null)
                    {
                        RowIdx = FDataView.Find(FSelectedCostCentre.CostCentreRow.CostCentreCode) + 1;
                    }

                    FParentForm.FIAmUpdating++;
                    grdCostCentres.SelectRowInGrid(RowIdx);
                    FParentForm.FIAmUpdating--;
                }
            }
        }

        private void InitializeManualCode()
        {
            // The auto-generated code requires that the grid be named grdDetails (for filter/find), but that doesn't work for another part of the autogenerated code!
            // So we make grdDetails reference grdSuppliers here at initialization
            grdDetails = grdCostCentres;
        }

        /// <summary>
        /// Perform initialisation
        /// (Actually called earlier than the parent RunOnceOnActivationManual)
        /// </summary>
        public void RunOnceOnActivationManual(TFrmGLCostCentreHierarchy ParentForm)
        {
            FParentForm = ParentForm;
            grdCostCentres.Selection.SelectionChanged += Selection_SelectionChanged;
        }

        void Selection_SelectionChanged(object sender, SourceGrid.RangeRegionChangedEventArgs e)
        {
            if (FParentForm.FIAmUpdating == 0)
            {
                int previousRowId = FPrevRowChangedRow;
                int newRowId = grdCostCentres.Selection.ActivePosition.Row;
                DataRowView rowView = (DataRowView)grdCostCentres.Rows.IndexToDataSourceRow(newRowId);

                if (rowView == null)
                {
                    FPreviouslySelectedDetailRow = null;
                    FParentForm.SetSelectedCostCentre(null);
                    FParentForm.PopulateControlsAfterRowSelection();
                    Console.WriteLine("Selected row is NULL");
                }
                else
                {
                    FPreviouslySelectedDetailRow = rowView.Row;
                    String SelectedCostCentreCode = ((ACostCentreRow)rowView.Row).CostCentreCode;
                    FParentForm.SetSelectedCostCentreCode(SelectedCostCentreCode);

                    if (previousRowId == -1)
                    {
                        FParentForm.PopulateControlsAfterRowSelection();
                    }

                    Console.WriteLine("Row is {0}", FPreviouslySelectedDetailRow.ItemArray[1]);
                }

                FPrevRowChangedRow = newRowId;
            }
        }

        /// <summary>
        /// Show all the data (CostCentre Code and description)
        /// </summary>
        public void PopulateListView(GLSetupTDS MainDS, Int32 LedgerNumber)
        {
//          FLedgerNumber = LedgerNumber;

            FDataView = new DataView(MainDS.ACostCentre);
            FDataView.Sort = "a_cost_centre_code_c";
            FDataView.AllowNew = false;
            grdCostCentres.DataSource = new DevAge.ComponentModel.BoundDataView(FDataView);
            grdCostCentres.Columns.Clear();
            grdCostCentres.AddTextColumn("Code", MainDS.ACostCentre.ColumnCostCentreCode);
            grdCostCentres.AddTextColumn("Descr", MainDS.ACostCentre.ColumnCostCentreName);
        }

        /// <summary>
        /// Method to collapse the filter panel if it is open
        /// </summary>
        public void CollapseFilterFind()
        {
            if (pnlFilterAndFind.Width > 0)
            {
                // Get the current row
                DataRow currentRow = FPreviouslySelectedDetailRow;

                FFilterAndFindObject.ToggleFilter();
                FParentForm.SetSelectedCostCentreCode(currentRow.ItemArray[1].ToString());
            }
        }

        private void FilterToggledManual(bool AFilterPanelIsCollapsed)
        {
            if (FIsFilterPanelInitialised)
            {
                return;
            }

            if (!AFilterPanelIsCollapsed)
            {
                FFilterTxtCostCentreCode = (TextBox)FFilterAndFindObject.FilterPanelControls.FindControlByName("txtCostCentreCode");
                FFilterCmbCostCentreType = (TCmbAutoComplete)FFilterAndFindObject.FilterPanelControls.FindControlByName("cmbCostCentreType");
                FFilterTxtCostCentreName = (TextBox)FFilterAndFindObject.FilterPanelControls.FindControlByName("txtCostCentreName");
                FFilterChkActive = (CheckBox)FFilterAndFindObject.FilterPanelControls.FindControlByName("chkActive");

                FIsFilterPanelInitialised = true;
            }
        }

        private void ApplyFilterManual(ref string AFilterString)
        {
            string filter = String.Empty;

            if (FFilterTxtCostCentreCode.Text != String.Empty)
            {
                JoinAndAppend(ref filter, String.Format("(a_cost_centre_code_c LIKE '%{0}%')", FFilterTxtCostCentreCode.Text));
            }

            if (FFilterCmbCostCentreType.Text != String.Empty)
            {
                JoinAndAppend(ref filter, String.Format("(a_cost_centre_type_c LIKE '{0}')", FFilterCmbCostCentreType.Text));
            }

            if (FFilterTxtCostCentreName.Text != String.Empty)
            {
                JoinAndAppend(ref filter, String.Format("(a_cost_centre_name_c LIKE '%{0}%')", FFilterTxtCostCentreName.Text));
            }

            if (FFilterChkActive.CheckState != CheckState.Indeterminate)
            {
                JoinAndAppend(ref filter, String.Format("(a_cost_centre_active_flag_l={0})", FFilterChkActive.Checked ? 1 : 0));
            }

            AFilterString = filter;
        }

        private void JoinAndAppend(ref string AStringToExtend, string AStringToAppend)
        {
            if (AStringToExtend.Length > 0)
            {
                AStringToExtend += " AND ";
            }

            AStringToExtend += AStringToAppend;
        }

        private bool IsMatchingRowManual(DataRow ARow)
        {
            return false;
        }

        /// <summary>
        /// Interface method
        /// </summary>
        public void SelectRowInGrid(int ARowToSelect)
        {
            grdDetails.SelectRowInGrid(ARowToSelect, true);
        }
    }
}