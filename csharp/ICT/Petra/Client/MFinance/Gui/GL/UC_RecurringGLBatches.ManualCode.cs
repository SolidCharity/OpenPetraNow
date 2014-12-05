//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       wolfgangb
//
// Copyright 2004-2012 by OM International
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
using System.Windows.Forms;
using System.IO;
using System.Xml;
using System.Data;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections;

using GNU.Gettext;

using Ict.Common;
using Ict.Common.Controls;
using Ict.Common.Data;
using Ict.Common.IO;
using Ict.Common.Verification;
using Ict.Common.Remoting.Client;

using Ict.Petra.Client.App.Core;
using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.MFinance.Logic;

using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.GL.Data;
using Ict.Petra.Shared.MFinance.Validation;
using Ict.Petra.Shared.Interfaces.MFinance;

namespace Ict.Petra.Client.MFinance.Gui.GL
{
    public partial class TUC_RecurringGLBatches
    {
        private Int32 FLedgerNumber = -1;
        private Int32 FSelectedBatchNumber = -1;
        private DateTime FDefaultDate = DateTime.Today;
        //private bool FBatchesLoaded = false;
        private bool FInitialFocusActionComplete = false;

        private GLSetupTDS FCacheDS;
        private ACostCentreTable FCostCentreTable = null;
        private AAccountTable FAccountTable = null;

        private void InitialiseControls()
        {
            //Leave in for future use
        }

        /// <summary>
        /// load the batches into the grid
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        public void LoadBatches(Int32 ALedgerNumber)
        {
            //FBatchesLoaded = false;
            InitialiseControls();

            FLedgerNumber = ALedgerNumber;

            if ((FPetraUtilsObject == null) || FPetraUtilsObject.SuppressChangeDetection)
            {
                return;
            }

            FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadARecurringBatch(FLedgerNumber, TFinanceBatchFilterEnum.fbfAll));

            if (FCacheDS == null)
            {
                FCacheDS = TRemote.MFinance.GL.WebConnectors.LoadAAnalysisAttributes(FLedgerNumber, false);
            }

            btnNew.Enabled = true;

            FPreviouslySelectedDetailRow = null;
            grdDetails.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ARecurringBatch.DefaultView);

            if (grdDetails.Rows.Count > 1)
            {
                SelectRowInGrid(1);
                ((TFrmRecurringGLBatch) this.ParentForm).EnableJournals();
                AutoEnableTransTabForBatch();
            }
            else
            {
                ClearControls();
                ((TFrmRecurringGLBatch) this.ParentForm).DisableJournals();
                ((TFrmRecurringGLBatch) this.ParentForm).DisableTransactions();
            }

            //Load all analysis attribute values
            ShowData();

            UpdateChangeableStatus();
            UpdateRecordNumberDisplay();
            SetAccountCostCentreTableVariables();

            //FBatchesLoaded = true;
        }

        /// reset the control
        public void ClearCurrentSelection()
        {
            if (FPetraUtilsObject.HasChanges)
            {
                GetDataFromControls();
            }

            this.FPreviouslySelectedDetailRow = null;
            ShowData();
        }

        /// <summary>
        /// Returns FMainDS
        /// </summary>
        /// <returns></returns>
        public GLBatchTDS RecurringBatchFMainDS()
        {
            return FMainDS;
        }

        /// <summary>
        /// Enable the transaction tab
        /// </summary>
        public void AutoEnableTransTabForBatch()
        {
            bool EnableTransTab = false;

            //If a single journal exists and it is not status=Cancelled then enable transactions tab
            if ((FPreviouslySelectedDetailRow != null) && (FPreviouslySelectedDetailRow.LastJournal == 1))
            {
                LoadJournalsForCurrentBatch();

                EnableTransTab = (FMainDS.ARecurringJournal.DefaultView.Count > 0);
            }

            ((TFrmRecurringGLBatch) this.ParentForm).EnableTransactions(EnableTransTab);
        }

        private void LoadJournalsForCurrentBatch()
        {
            //Current Batch number
            Int32 BatchNumber = FPreviouslySelectedDetailRow.BatchNumber;

            if (FMainDS.ARecurringJournal != null)
            {
                FMainDS.ARecurringJournal.DefaultView.RowFilter = String.Format("{0}={1}",
                    ARecurringTransactionTable.GetBatchNumberDBName(),
                    BatchNumber);

                if (FMainDS.ARecurringJournal.DefaultView.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadARecurringJournal(FLedgerNumber, BatchNumber));
                }
            }
        }

        private void ShowDataManual()
        {
            if (FLedgerNumber == -1)
            {
                EnableButtonControl(false);
            }
        }

        private void UpdateChangeableStatus()
        {
            Boolean allowSubmit = (FPreviouslySelectedDetailRow != null)
                                  && (grdDetails.Rows.Count > 1);

            FPetraUtilsObject.EnableAction("actSubmitBatch", allowSubmit);
            FPetraUtilsObject.EnableAction("actDelete", allowSubmit);
            pnlDetails.Enabled = allowSubmit;
            pnlDetailsProtected = !allowSubmit;

            if ((FPreviouslySelectedDetailRow == null) && (((TFrmRecurringGLBatch) this.ParentForm) != null))
            {
                ((TFrmRecurringGLBatch) this.ParentForm).DisableJournals();
            }
        }

        private void ValidateDataDetailsManual(ARecurringBatchRow ARow)
        {
            if (ARow == null)
            {
                return;
            }

            TVerificationResultCollection VerificationResultCollection = FPetraUtilsObject.VerificationResultCollection;

            ParseHashTotal(ARow);

            TSharedFinanceValidation_GL.ValidateRecurringGLBatchManual(this, ARow, ref VerificationResultCollection,
                FValidationControlsDict);

            //TODO: remove this once database definition is set for Batch Description to be NOT NULL
            // Description is mandatory then make sure it is set
            if (txtDetailBatchDescription.Text.Length == 0)
            {
                DataColumn ValidationColumn;
                TVerificationResult VerificationResult = null;
                object ValidationContext;

                ValidationColumn = ARow.Table.Columns[ARecurringBatchTable.ColumnBatchDescriptionId];
                ValidationContext = String.Format("Recurring Batch number {0}",
                    ARow.BatchNumber);

                VerificationResult = TStringChecks.StringMustNotBeEmpty(ARow.BatchDescription,
                    "Description of " + ValidationContext,
                    this, ValidationColumn, null);

                // Handle addition/removal to/from TVerificationResultCollection
                VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn, true);
            }
        }

        private void ParseHashTotal(ARecurringBatchRow ARow)
        {
            decimal correctHashValue = 0m;

            if ((txtDetailBatchControlTotal.NumberValueDecimal == null) || !txtDetailBatchControlTotal.NumberValueDecimal.HasValue)
            {
                correctHashValue = 0m;
            }
            else
            {
                correctHashValue = txtDetailBatchControlTotal.NumberValueDecimal.Value;
            }

            txtDetailBatchControlTotal.NumberValueDecimal = correctHashValue;
            ARow.BatchControlTotal = correctHashValue;
        }

        private void ShowDetailsManual(ARecurringBatchRow ARow)
        {
            AutoEnableTransTabForBatch();
            grdDetails.TabStop = (ARow != null);

            if (ARow == null)
            {
                pnlDetails.Enabled = false;
                ((TFrmRecurringGLBatch) this.ParentForm).DisableJournals();
                ((TFrmRecurringGLBatch) this.ParentForm).DisableTransactions();
                EnableButtonControl(false);
                ClearControls();
                return;
            }

            FPetraUtilsObject.DetailProtectedMode = false;

            FSelectedBatchNumber = ARow.BatchNumber;

            UpdateChangeableStatus();
            ((TFrmRecurringGLBatch) this.ParentForm).EnableJournals();
        }

        /// <summary>
        /// This routine is called by a double click on a batch row, which means: Open the
        /// Journal Tab of this batch.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShowJournalTab(Object sender, EventArgs e)
        {
            ((TFrmRecurringGLBatch)ParentForm).SelectTab(TFrmRecurringGLBatch.eGLTabs.RecurringJournals);
        }

        /// <summary>
        /// Controls the enabled status of the Delete and Submit buttons
        /// </summary>
        /// <param name="AEnable"></param>
        private void EnableButtonControl(bool AEnable)
        {
            if (AEnable)
            {
                if (!pnlDetails.Enabled)
                {
                    pnlDetails.Enabled = true;
                }
            }

            btnSubmitBatch.Enabled = AEnable;
            btnDelete.Enabled = AEnable;
        }

        /// <summary>
        /// add a new batch
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewRow(System.Object sender, EventArgs e)
        {
            CreateNewARecurringBatch();

            pnlDetails.Enabled = true;
            EnableButtonControl(true);

            ARecurringBatchRow newBatchRow = GetSelectedDetailRow();
            Int32 yearNumber = 0;
            Int32 periodNumber = 0;

            if (GetAccountingYearPeriodByDate(FLedgerNumber, FDefaultDate, out yearNumber, out periodNumber))
            {
                newBatchRow.BatchPeriod = periodNumber;
            }

            newBatchRow.DateEffective = FDefaultDate;

            FSelectedBatchNumber = newBatchRow.BatchNumber;

            //Enable the Journals if not already enabled
            ((TFrmRecurringGLBatch)ParentForm).EnableJournals();

            ((TFrmRecurringGLBatch) this.ParentForm).SaveChanges();
        }

        private bool GetAccountingYearPeriodByDate(Int32 ALedgerNumber, DateTime ADate, out Int32 AYear, out Int32 APeriod)
        {
            return TRemote.MFinance.GL.WebConnectors.GetAccountingYearPeriodByDate(ALedgerNumber, ADate, out AYear, out APeriod);
        }

        private bool PreDeleteManual(ARecurringBatchRow ARowToDelete, ref string ADeletionQuestion)
        {
            bool allowDeletion = true;

            if (FPreviouslySelectedDetailRow != null)
            {
                ADeletionQuestion = String.Format(Catalog.GetString("Are you sure you want to delete recurring Batch {0}?"),
                    ARowToDelete.BatchNumber);
            }

            return allowDeletion;
        }

        /// <summary>
        /// Deletes the current row and optionally populates a completion message
        /// </summary>
        /// <param name="ARowToDelete">the currently selected row to delete</param>
        /// <param name="ACompletionMessage">if specified, is the deletion completion message</param>
        /// <returns>true if row deletion is successful</returns>
        private bool DeleteRowManual(ARecurringBatchRow ARowToDelete, ref string ACompletionMessage)
        {
            int BatchNumber = ARowToDelete.BatchNumber;

            // Delete on client side data through views that is already loaded. Data that is not
            // loaded yet will be deleted with cascading delete on server side so we don't have
            // to worry about this here.

            ACompletionMessage = String.Format(Catalog.GetString("Batch no.: {0} deleted successfully."),
                BatchNumber);

            // Delete the associated recurring transaction analysis attributes
            DataView viewRecurringTransAnalAttrib = new DataView(FMainDS.ARecurringTransAnalAttrib);
            viewRecurringTransAnalAttrib.RowFilter = String.Format("{0} = {1} AND {2} = {3}",
                ARecurringTransAnalAttribTable.GetLedgerNumberDBName(),
                FLedgerNumber,
                ARecurringTransAnalAttribTable.GetBatchNumberDBName(),
                BatchNumber);

            foreach (DataRowView row in viewRecurringTransAnalAttrib)
            {
                row.Delete();
            }

            // Delete the associated recurring transactions
            DataView viewRecurringTransaction = new DataView(FMainDS.ARecurringTransaction);
            viewRecurringTransaction.RowFilter = String.Format("{0} = {1} AND {2} = {3}",
                ARecurringTransactionTable.GetLedgerNumberDBName(),
                FLedgerNumber,
                ARecurringTransactionTable.GetBatchNumberDBName(),
                BatchNumber);

            foreach (DataRowView row in viewRecurringTransaction)
            {
                row.Delete();
            }

            // Delete the associated recurring journals
            DataView viewRecurringJournal = new DataView(FMainDS.ARecurringJournal);
            viewRecurringJournal.RowFilter = String.Format("{0} = {1} AND {2} = {3}",
                ARecurringJournalTable.GetLedgerNumberDBName(),
                FLedgerNumber,
                ARecurringJournalTable.GetBatchNumberDBName(),
                BatchNumber);

            foreach (DataRowView row in viewRecurringJournal)
            {
                row.Delete();
            }

            // Delete the recurring batch row.
            ARowToDelete.Delete();

            UpdateRecordNumberDisplay();

            return true;
        }

        /// <summary>
        /// Code to be run after the deletion process
        /// </summary>
        /// <param name="ARowToDelete">the row that was/was to be deleted</param>
        /// <param name="AAllowDeletion">whether or not the user was permitted to delete</param>
        /// <param name="ADeletionPerformed">whether or not the deletion was performed successfully</param>
        /// <param name="ACompletionMessage">if specified, is the deletion completion message</param>
        private void PostDeleteManual(ARecurringBatchRow ARowToDelete,
            bool AAllowDeletion,
            bool ADeletionPerformed,
            string ACompletionMessage)
        {
            /*Code to execute after the delete has occurred*/
            if (ADeletionPerformed && (ACompletionMessage.Length > 0))
            {
                //causes saving issues
                //UpdateLedgerTableSettings();
                ((TFrmRecurringGLBatch) this.ParentForm).SaveChanges();
                MessageBox.Show(ACompletionMessage, Catalog.GetString("Deletion Completed"));
            }

            UpdateChangeableStatus();

            if (!pnlDetails.Enabled)         //set by FocusedRowChanged if grdDetails.Rows.Count < 2
            {
                ClearControls();
            }

            if (grdDetails.Rows.Count > 1)
            {
                ((TFrmRecurringGLBatch)ParentForm).EnableJournals();
            }
            else
            {
                ((TFrmRecurringGLBatch)ParentForm).GetJournalsControl().ClearCurrentSelection();
                ((TFrmRecurringGLBatch)ParentForm).DisableJournals();
            }

            SetInitialFocus();
        }

        private void UpdateLedgerTableSettings()
        {
            int LedgerLastRecurringGLBatchNumber = 0;

            //Update the last recurring GL batch number
            DataView RecurringGLBatchDV = new DataView(FMainDS.ARecurringBatch);

            RecurringGLBatchDV.RowFilter = string.Empty;
            RecurringGLBatchDV.Sort = string.Format("{0} DESC",
                ARecurringBatchTable.GetBatchNumberDBName());

            //Recurring batch numbers can be reused so reset current highest number
            if (RecurringGLBatchDV.Count > 0)
            {
                LedgerLastRecurringGLBatchNumber = (int)(RecurringGLBatchDV[0][ARecurringBatchTable.GetBatchNumberDBName()]);
            }

            if (FMainDS.ALedger[0].LastRecurringBatchNumber != LedgerLastRecurringGLBatchNumber)
            {
                FMainDS.ALedger[0].LastRecurringBatchNumber = LedgerLastRecurringGLBatchNumber;
            }
        }

        private void ClearControls()
        {
            try
            {
                FPetraUtilsObject.DisableDataChangedEvent();
                txtDetailBatchDescription.Clear();
                txtDetailBatchControlTotal.NumberValueDecimal = 0;
            }
            finally
            {
                FPetraUtilsObject.EnableDataChangedEvent();
            }
        }

        private int GetDataTableRowIndexByPrimaryKeys(int ALedgerNumber, int ABatchNumber)
        {
            int rowPos = 0;
            bool batchFound = false;

            foreach (DataRowView rowView in FMainDS.ARecurringBatch.DefaultView)
            {
                ARecurringBatchRow row = (ARecurringBatchRow)rowView.Row;

                if ((row.LedgerNumber == ALedgerNumber) && (row.BatchNumber == ABatchNumber))
                {
                    batchFound = true;
                    break;
                }

                rowPos++;
            }

            if (!batchFound)
            {
                rowPos = 0;
            }

            //remember grid is out of sync with DataView by 1 because of grid header rows
            return rowPos + 1;
        }

        /// <summary>
        /// Set focus to the gid controltab
        /// </summary>
        public void FocusGrid()
        {
            if ((grdDetails != null) && grdDetails.CanFocus)
            {
                grdDetails.Focus();
            }
        }

        /// <summary>
        /// Shows the Filter/Find UserControl and switches to the Find Tab.
        /// </summary>
        public void ShowFindPanel()
        {
            if (FFilterAndFindObject.FilterFindPanel == null)
            {
                FFilterAndFindObject.ToggleFilter();
            }

            FFilterAndFindObject.FilterFindPanel.DisplayFindTab();
        }

        /// <summary>
        /// Sets the initial focus to the grid or the New button depending on the row count
        /// </summary>
        public void SetInitialFocus()
        {
            if (FInitialFocusActionComplete)
            {
                return;
            }

            if (grdDetails.CanFocus)
            {
                if (grdDetails.Rows.Count < 2)
                {
                    btnNew.Focus();
                }
                else if (grdDetails.CanFocus)
                {
                    grdDetails.Focus();
                }

                FInitialFocusActionComplete = true;
            }
        }

        private void RunOnceOnParentActivationManual()
        {
            grdDetails.DoubleClickCell += new TDoubleClickCellEventHandler(this.ShowJournalTab);
            grdDetails.DataSource.ListChanged += new System.ComponentModel.ListChangedEventHandler(DataSource_ListChanged);
        }

        private void DataSource_ListChanged(object sender, System.ComponentModel.ListChangedEventArgs e)
        {
            if (grdDetails.CanFocus && (grdDetails.Rows.Count > 1))
            {
                grdDetails.AutoResizeGrid();
            }
        }

        private int CurrentRowIndex()
        {
            int rowIndex = -1;

            SourceGrid.RangeRegion selectedRegion = grdDetails.Selection.GetSelectionRegion();

            if ((selectedRegion != null) && (selectedRegion.GetRowsIndex().Length > 0))
            {
                rowIndex = selectedRegion.GetRowsIndex()[0];
            }

            return rowIndex;
        }

        private void SubmitBatch(System.Object sender, EventArgs e)
        {
            if (FPreviouslySelectedDetailRow == null)
            {
                MessageBox.Show(Catalog.GetString("Please select a Batch before submitting."));
                return;
            }

            if (FPetraUtilsObject.HasChanges)
            {
                // save first, then submit
                if (!((TFrmRecurringGLBatch)ParentForm).SaveChanges())
                {
                    // saving failed, therefore do not try to post
                    MessageBox.Show(Catalog.GetString(
                            "The recurring batch was not submitted due to problems during saving; ") + Environment.NewLine +
                        Catalog.GetString("Please fix the batch first and then submit it."),
                        Catalog.GetString("Submit Failure"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if ((FPreviouslySelectedDetailRow.BatchControlTotal != 0)
                && (FPreviouslySelectedDetailRow.BatchDebitTotal != FPreviouslySelectedDetailRow.BatchControlTotal))
            {
                MessageBox.Show(String.Format(Catalog.GetString(
                            "The recurring gl batch total ({0}) for batch {1} does not equal the hash total ({2})."),
                        FPreviouslySelectedDetailRow.BatchDebitTotal,
                        FPreviouslySelectedDetailRow.BatchNumber,
                        FPreviouslySelectedDetailRow.BatchControlTotal));

                txtDetailBatchControlTotal.Focus();
                txtDetailBatchControlTotal.SelectAll();
                return;
            }

            if (!LoadAllBatchData() || !AllowInactiveFieldValues())
            {
                return;
            }

            TFrmRecurringGLBatchSubmit submitForm = new TFrmRecurringGLBatchSubmit(FPetraUtilsObject.GetForm());
            try
            {
                ParentForm.ShowInTaskbar = false;
                submitForm.MainDS = FMainDS;
                submitForm.BatchRow = FPreviouslySelectedDetailRow;
                submitForm.ShowDialog();
            }
            finally
            {
                submitForm.Dispose();
                ParentForm.ShowInTaskbar = true;
            }
        }

        private Boolean LoadAllBatchData(int ABatchNumber = 0)
        {
            bool RetVal = false;

            DataView JournalDV = new DataView(FMainDS.ARecurringJournal);
            DataView TransDV = new DataView(FMainDS.ARecurringTransaction);

            bool NoJournalRows = true;
            bool NoTransRows = true;

            if ((ABatchNumber == 0) && (FPreviouslySelectedDetailRow != null))
            {
                ABatchNumber = FPreviouslySelectedDetailRow.BatchNumber;
            }
            else if (ABatchNumber == 0)
            {
                return RetVal;
            }

            try
            {
                // now load journals/transactions for this batch, if necessary, so we know if exchange rate needs to be set in case of different currency
                JournalDV.RowFilter = String.Format("{0}={1}",
                    ARecurringJournalTable.GetBatchNumberDBName(),
                    FSelectedBatchNumber);
                TransDV.RowFilter = String.Format("{0}={1}",
                    ARecurringTransactionTable.GetBatchNumberDBName(),
                    FSelectedBatchNumber);

                if (JournalDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadARecurringJournalAndContent(FLedgerNumber, FSelectedBatchNumber));
                }
                else if (TransDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadARecurringTransactionAndContent(FLedgerNumber, FSelectedBatchNumber));
                }

                NoJournalRows = (JournalDV.Count == 0);
                NoTransRows = (TransDV.Count == 0);

                if (NoJournalRows && NoTransRows)
                {
                    if (MessageBox.Show(String.Format(Catalog.GetString("The recurring gl batch {0} is empty. Do you still want to submit?"),
                                FPreviouslySelectedDetailRow.BatchNumber),
                            Catalog.GetString("Submit Empty Batch"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) == DialogResult.No)
                    {
                        return RetVal;
                    }
                }
                else if (!NoJournalRows && NoTransRows)
                {
                    if (MessageBox.Show(String.Format(Catalog.GetString(
                                    "The recurring gl batch {0} contains empty journals. Do you still want to submit?"),
                                FPreviouslySelectedDetailRow.BatchNumber),
                            Catalog.GetString("Submit Empty Journals"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) == DialogResult.No)
                    {
                        return RetVal;
                    }
                }
                else if (NoJournalRows && !NoTransRows)
                {
                    MessageBox.Show(String.Format(Catalog.GetString(
                                "The recurring gl batch {0} contains orphaned transactions. PLEASE DELETE THE RECURRING BATCH AND RECREATE!"),
                            FPreviouslySelectedDetailRow.BatchNumber),
                        Catalog.GetString("Orphaned Data"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return RetVal;
                }

                RetVal = true;
            }
            catch (Exception)
            {
                throw;
            }

            return RetVal;
        }

        private bool AllowInactiveFieldValues()
        {
            bool RetVal = false;

            DataView TransDV = new DataView(FMainDS.ARecurringTransaction);
            DataView AttribDV = new DataView(FMainDS.ARecurringTransAnalAttrib);

            try
            {
                TransDV.RowFilter = String.Format("{0}={1}",
                    ARecurringTransactionTable.GetBatchNumberDBName(),
                    FSelectedBatchNumber);
                AttribDV.RowFilter = String.Format("{0}={1}",
                    ARecurringTransAnalAttribTable.GetBatchNumberDBName(),
                    FSelectedBatchNumber);

                //Check for inactive data field values
                foreach (DataRowView drv in TransDV)
                {
                    ARecurringTransactionRow transRow = (ARecurringTransactionRow)drv.Row;

                    if (!AccountIsActive(transRow.AccountCode) || !CostCentreIsActive(transRow.CostCentreCode))
                    {
                        if (MessageBox.Show(String.Format(Catalog.GetString(
                                        "Recurring batch no. {0} contains an inactive account or cost centre code in journal {1}, transaction {2}. Do you still want to submit the batch?"),
                                    FSelectedBatchNumber,
                                    transRow.JournalNumber,
                                    transRow.TransactionNumber),
                                Catalog.GetString("Inactive Account/Cost Centre Code"), MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning,
                                MessageBoxDefaultButton.Button2) == DialogResult.No)
                        {
                            return RetVal;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                foreach (DataRowView drv2 in AttribDV)
                {
                    ARecurringTransAnalAttribRow analAttribRow = (ARecurringTransAnalAttribRow)drv2.Row;

                    if (!AnalysisCodeIsActive(analAttribRow.AccountCode,
                            analAttribRow.AnalysisTypeCode)
                        || !AnalysisAttributeValueIsActive(analAttribRow.AnalysisTypeCode, analAttribRow.AnalysisAttributeValue))
                    {
                        if (MessageBox.Show(String.Format(Catalog.GetString(
                                        "Recurring batch no. {0} contains an inactive analysis attribute code/value in journal {1}, transaction {2}. Do you still want to submit the batch?"),
                                    FSelectedBatchNumber,
                                    analAttribRow.JournalNumber,
                                    analAttribRow.TransactionNumber),
                                Catalog.GetString("Inactive Analysis Attribute Code/Value"), MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.No)
                        {
                            return RetVal;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                RetVal = true;
            }
            catch (Exception)
            {
                throw;
            }

            return RetVal;
        }

        private void SetAccountCostCentreTableVariables()
        {
            //Populate CostCentreList variable
            DataTable costCentreList = TDataCache.TMFinance.GetCacheableFinanceTable(TCacheableFinanceTablesEnum.CostCentreList,
                FLedgerNumber);

            ACostCentreTable tmpCostCentreTable = new ACostCentreTable();

            FMainDS.Tables.Add(tmpCostCentreTable);
            DataUtilities.ChangeDataTableToTypedDataTable(ref costCentreList, FMainDS.Tables[tmpCostCentreTable.TableName].GetType(), "");
            FMainDS.RemoveTable(tmpCostCentreTable.TableName);

            FCostCentreTable = (ACostCentreTable)costCentreList;

            //Populate AccountList variable
            DataTable accountList = TDataCache.TMFinance.GetCacheableFinanceTable(TCacheableFinanceTablesEnum.AccountList, FLedgerNumber);

            AAccountTable tmpAccountTable = new AAccountTable();
            FMainDS.Tables.Add(tmpAccountTable);
            DataUtilities.ChangeDataTableToTypedDataTable(ref accountList, FMainDS.Tables[tmpAccountTable.TableName].GetType(), "");
            FMainDS.RemoveTable(tmpAccountTable.TableName);

            FAccountTable = (AAccountTable)accountList;
        }

        private bool AnalysisCodeIsActive(String AAccountCode, String AAnalysisCode = "")
        {
            bool retVal = true;

            if ((AAnalysisCode == string.Empty) || (AAccountCode == string.Empty))
            {
                return retVal;
            }

            DataView dv = new DataView(FCacheDS.AAnalysisAttribute);

            dv.RowFilter = String.Format("{0}={1} AND {2}='{3}' AND {4}='{5}' AND {6}=true",
                AAnalysisAttributeTable.GetLedgerNumberDBName(),
                FLedgerNumber,
                AAnalysisAttributeTable.GetAccountCodeDBName(),
                AAccountCode,
                AAnalysisAttributeTable.GetAnalysisTypeCodeDBName(),
                AAnalysisCode,
                AAnalysisAttributeTable.GetActiveDBName());

            retVal = (dv.Count > 0);

            return retVal;
        }

        private bool AnalysisAttributeValueIsActive(String AAnalysisCode = "", String AAnalysisAttributeValue = "")
        {
            bool retVal = true;

            if ((AAnalysisCode == string.Empty) || (AAnalysisAttributeValue == string.Empty))
            {
                return retVal;
            }

            DataView dv = new DataView(FCacheDS.AFreeformAnalysis);

            dv.RowFilter = String.Format("{0}='{1}' AND {2}='{3}' AND {4}=true",
                AFreeformAnalysisTable.GetAnalysisTypeCodeDBName(),
                AAnalysisCode,
                AFreeformAnalysisTable.GetAnalysisValueDBName(),
                AAnalysisAttributeValue,
                AFreeformAnalysisTable.GetActiveDBName());

            retVal = (dv.Count > 0);

            return retVal;
        }

        private bool AccountIsActive(string AAccountCode)
        {
            bool retVal = true;

            AAccountRow currentAccountRow = null;

            if (FAccountTable != null)
            {
                currentAccountRow = (AAccountRow)FAccountTable.Rows.Find(new object[] { FLedgerNumber, AAccountCode });
            }

            if (currentAccountRow != null)
            {
                retVal = currentAccountRow.AccountActiveFlag;
            }

            return retVal;
        }

        private bool CostCentreIsActive(string ACostCentreCode)
        {
            bool retVal = true;

            ACostCentreRow currentCostCentreRow = null;

            if (FCostCentreTable != null)
            {
                currentCostCentreRow = (ACostCentreRow)FCostCentreTable.Rows.Find(new object[] { FLedgerNumber, ACostCentreCode });
            }

            if (currentCostCentreRow != null)
            {
                retVal = currentCostCentreRow.CostCentreActiveFlag;
            }

            return retVal;
        }
    }
}