//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       wolfgangb
//
// Copyright 2004-2011 by OM International
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
using System.Windows.Forms;
using System.Collections.Generic;
using Ict.Common;
using Ict.Common.Data;

using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.CommonForms;
using Ict.Petra.Client.MFinance.Logic;
using Ict.Petra.Shared.MFinance.Gift.Data;

namespace Ict.Petra.Client.MFinance.Gui.Gift
{
    public partial class TFrmRecurringGiftBatch : IFrmPetraEditManual
    {
        private Int32 FLedgerNumber;
        private int standardTabIndex = 0;

        /// <summary>
        /// use this ledger
        /// </summary>
        public Int32 LedgerNumber
        {
            set
            {
                FLedgerNumber = value;

                // setting the ledger number on the batch screen will automatically trigger loading the batches for the current year
                ucoRecurringBatches.LedgerNumber = value;

                this.Text += " - " + TFinanceControls.GetLedgerNumberAndName(FLedgerNumber);

                //Enable below if want code to run before standard Save() is executed
                FPetraUtilsObject.DataSavingStarted += new TDataSavingStartHandler(FPetraUtilsObject_DataSavingStarted);
            }
        }

        /// <summary>
        /// show the actual data of the database after server has changed data
        /// </summary>
        public void RefreshAll()
        {
            ucoRecurringBatches.RefreshAllData();
        }

        private void FileSaveManual(object sender, EventArgs e)
        {
            SaveChangesManual();
        }

        /// <summary>
        /// Check for ExWorkers before saving
        /// </summary>
        /// <returns>True if Save is successful</returns>
        public bool SaveChangesManual()
        {
            return SaveChangesManual(TExtraGiftBatchChecks.GiftBatchAction.SAVING);
        }

        /// <summary>
        /// Check for ExWorkers before saving or cancelling
        /// </summary>
        /// <returns>True if Save is successful</returns>
        public bool SaveChangesManual(TExtraGiftBatchChecks.GiftBatchAction AAction)
        {
            GetDataFromControls();

            // first alert the user to any recipients who are Ex-Workers
            if (TExtraGiftBatchChecks.CanContinueWithAnyExWorkers(AAction, FMainDS, FPetraUtilsObject))
            {
                return SaveChanges();
            }

            return false;
        }

        /// <summary>
        /// Check for Ex-Worker before saving and submitting
        /// </summary>
        /// <param name="ASubmittingGiftDetails">GiftDetails for the recurring batch that is to be submitted</param>
        /// <param name="ACancelledDueToExWorker">True if batch posting has been cancelled by the user because of an Ex-Worker recipient</param>
        /// <returns>True if Save is successful</returns>
        public bool SaveChangesForSubmitting(DataTable ASubmittingGiftDetails, out bool ACancelledDueToExWorker)
        {
            GetDataFromControls();

            // first alert the user to any recipients who are Ex-Workers
            ACancelledDueToExWorker = !TExtraGiftBatchChecks.CanContinueWithAnyExWorkers(
                TExtraGiftBatchChecks.GiftBatchAction.SUBMITTING, FMainDS, FPetraUtilsObject, ASubmittingGiftDetails);

            if (!ACancelledDueToExWorker)
            {
                return SaveChanges();
            }

            return false;
        }

        // Before the dataset is saved, check for correlation between batch and transactions
        private void FPetraUtilsObject_DataSavingStarted(object Sender, EventArgs e)
        {
            ucoRecurringBatches.CheckBeforeSaving();
            ucoRecurringTransactions.CheckBeforeSaving();
        }

        private void InitializeManualCode()
        {
            tabGiftBatch.Selecting += new TabControlCancelEventHandler(TabSelectionChanging);
            this.tpgRecurringTransactions.Enabled = false;

            // change the event that gets called when 'Save' is clicked (i.e. changed from generated code)
            tbbSave.Click -= FileSave;
            mniFileSave.Click -= FileSave;
            tbbSave.Click += FileSaveManual;
            mniFileSave.Click += FileSaveManual;
        }

        /// <summary>
        /// Handles the click event for filter/find.
        /// </summary>
        /// <param name="sender">Pass this on to the user control.</param>
        /// <param name="e">Not evaluated.</param>
        public void MniFilterFind_Click(object sender, System.EventArgs e)
        {
            switch (tabGiftBatch.SelectedIndex)
            {
                case (int)eGiftTabs.Batches:
                    ucoRecurringBatches.MniFilterFind_Click(sender, e);
                    break;

                case (int)eGiftTabs.Transactions:
                    ucoRecurringTransactions.ReconcileKeyMinistryFromCombo();
                    ucoRecurringTransactions.MniFilterFind_Click(sender, e);
                    break;
            }
        }

        private void TFrmRecurringGiftBatch_Load(object sender, EventArgs e)
        {
            FPetraUtilsObject.TFrmPetra_Load(sender, e);

            tabGiftBatch.SelectedIndex = standardTabIndex;
            TabSelectionChanged(null, null);

            this.Shown += delegate
            {
                // This will ensure the grid gets the focus when the screen is shown for the first time
                ucoRecurringBatches.SetInitialFocus();
            };
        }

        private void RunOnceOnActivationManual()
        {
            ucoRecurringBatches.Focus();
            HookupAllInContainer(ucoRecurringBatches);
            HookupAllInContainer(ucoRecurringTransactions);
        }

        /// <summary>
        /// activate the transactions tab and load the gift transactions of the batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns>True if new transactions were actually loaded, False if transactions have already been loaded for the ledger/batch</returns>
        public bool LoadTransactions(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            return this.ucoRecurringTransactions.LoadRecurringGifts(ALedgerNumber, ABatchNumber);
        }

        /// <summary>
        /// this should be called when all data is reloaded after posting
        /// </summary>
        public void ClearCurrentSelections()
        {
            if (this.ucoRecurringBatches != null)
            {
                this.ucoRecurringBatches.ClearCurrentSelection();
            }

            if (this.ucoRecurringTransactions != null)
            {
                this.ucoRecurringTransactions.ClearCurrentSelection();
            }
        }

        /// enable the transaction tab page
        public void EnableTransactions(bool AEnable = true)
        {
            this.tpgRecurringTransactions.Enabled = AEnable;
        }

        /// <summary>
        /// enable the transaction tab page
        /// </summary>
        public void DisableTransactions()
        {
            this.tpgRecurringTransactions.Enabled = false;
        }

        /// <summary>
        /// disable the batches tab
        /// </summary>
        public void DisableBatches()
        {
            this.tpgRecurringBatches.Enabled = false;
        }

        /// <summary>
        /// directly access the batches control
        /// </summary>
        public TUC_RecurringGiftBatches GetBatchControl()
        {
            return ucoRecurringBatches;
        }

        /// <summary>
        /// directly access the transactions control
        /// </summary>
        public TUC_RecurringGiftTransactions GetTransactionsControl()
        {
            return ucoRecurringTransactions;
        }

        /// this window contains 2 tabs
        public enum eGiftTabs
        {
            /// list of batches
            Batches,

            /// list of transactions
            Transactions
        };

        void TabSelectionChanging(object sender, TabControlCancelEventArgs e)
        {
            FPetraUtilsObject.VerificationResultCollection.Clear();

            if (!ValidateAllData(false, true))
            {
                e.Cancel = true;

                FPetraUtilsObject.VerificationResultCollection.FocusOnFirstErrorControlRequested = true;
            }
        }

        private void SelectTabManual(int ASelectedTabIndex)
        {
            if (ASelectedTabIndex == (int)eGiftTabs.Batches)
            {
                SelectTab(eGiftTabs.Batches);
            }
            else
            {
                SelectTab(eGiftTabs.Transactions);
            }
        }

        /// <summary>
        /// Switch to the given tab
        /// </summary>
        /// <param name="ATab"></param>
        public void SelectTab(eGiftTabs ATab)
        {
            FPetraUtilsObject.RestoreAdditionalWindowPositionProperties();

            if (ATab == eGiftTabs.Batches)
            {
                this.tabGiftBatch.SelectedTab = this.tpgRecurringBatches;
                this.tpgRecurringTransactions.Enabled = (ucoRecurringBatches.GetSelectedDetailRow() != null);
                this.ucoRecurringBatches.SetFocusToGrid();
            }
            else if (ATab == eGiftTabs.Transactions)
            {
                if (this.tpgRecurringTransactions.Enabled)
                {
                    // Note!! This call may result in this (SelectTab) method being called again (but no new transactions will be loaded the second time)
                    this.tabGiftBatch.SelectedTab = this.tpgRecurringTransactions;

                    ARecurringGiftBatchRow SelectedRow = ucoRecurringBatches.GetSelectedDetailRow();

                    // If there's only one GiftBatch row, I'll not require that the user has selected it!
                    if (FMainDS.ARecurringGiftBatch.Rows.Count == 1)
                    {
                        SelectedRow = FMainDS.ARecurringGiftBatch[0];
                    }

                    if (SelectedRow != null)
                    {
                        try
                        {
                            this.Cursor = Cursors.WaitCursor;

                            LoadTransactions(SelectedRow.LedgerNumber, SelectedRow.BatchNumber);
                        }
                        finally
                        {
                            this.Cursor = Cursors.Default;
                        }
                    }

                    ucoRecurringTransactions.FocusGrid();
                }
            }
        }

        /// <summary>
        /// Handler for command key processing
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.S | Keys.Control))
            {
                if (FPetraUtilsObject.HasChanges)
                {
                    SaveChangesManual();
                }

                return true;
            }

            if ((tabGiftBatch.SelectedTab == tpgRecurringBatches) && (ucoRecurringBatches.ProcessParentCmdKey(ref msg, keyData)))
            {
                return true;
            }
            else if ((tabGiftBatch.SelectedTab == tpgRecurringTransactions) && (ucoRecurringTransactions.ProcessParentCmdKey(ref msg, keyData)))
            {
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Ensure the data is loaded for the specified batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns>If transactions exist</returns>
        public Boolean EnsureGiftDataPresent(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            DataView TransDV = new DataView(FMainDS.ARecurringGiftDetail);

            TransDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                ARecurringGiftDetailTable.GetLedgerNumberDBName(),
                ALedgerNumber,
                ARecurringGiftDetailTable.GetBatchNumberDBName(),
                ABatchNumber);

            if (TransDV.Count == 0)
            {
                FMainDS.Merge(TRemote.MFinance.Gift.WebConnectors.LoadARecurringGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber, true));
            }

            return TransDV.Count > 0;
        }

        /// <summary>
        /// find a special gift detail
        /// </summary>
        public void FindGiftDetail(ARecurringGiftDetailRow gdr)
        {
            //TODO add to other forms
            //ucoRecurringBatches.SelectBatchNumber(gdr.BatchNumber);
            //ucoRecurringTransactions.SelectGiftDetailNumber(gdr.GiftTransactionNumber, gdr.DetailNumber);
            //standardTabIndex = 1;     // later we switch to the detail tab
        }

        private int GetChangedRecordCountManual(out string AMessage)
        {
            // For Gift Batch we will
            //  either get a change to N Batches
            //  or get changes to M transactions in N Batches
            List <Tuple <string, int>>TableAndCountList = new List <Tuple <string, int>>();
            int allChangesCount = 0;

            foreach (DataTable dt in FMainDS.Tables)
            {
                if (dt != null)
                {
                    int tableChangesCount = 0;

                    foreach (DataRow dr in dt.Rows)
                    {
                        if (dr.RowState != DataRowState.Unchanged)
                        {
                            tableChangesCount++;
                            allChangesCount++;
                        }
                    }

                    if (tableChangesCount > 0)
                    {
                        TableAndCountList.Add(new Tuple <string, int>(dt.TableName, tableChangesCount));
                    }
                }
            }

            // Now build up a sensible message
            AMessage = String.Empty;

            if (TableAndCountList.Count > 0)
            {
                if (TableAndCountList.Count == 1)
                {
                    // Only saving changes to batches
                    Tuple <string, int>TableAndCount = TableAndCountList[0];

                    AMessage = String.Format(Catalog.GetString("    You have made changes to the details of {0} {1}.{2}"),
                        TableAndCount.Item2,
                        Catalog.GetPluralString("batch", "batches", TableAndCount.Item2),
                        Environment.NewLine);
                }
                else
                {
                    // Saving changes to transactions as well
                    int nBatches = 0;
                    int nTransactions = 0;

                    foreach (Tuple <string, int>TableAndCount in TableAndCountList)
                    {
                        if (TableAndCount.Item1.Equals(ARecurringGiftBatchTable.GetTableName()))
                        {
                            nBatches = TableAndCount.Item2;
                        }
                        else if (TableAndCount.Item2 > nTransactions)
                        {
                            nTransactions = TableAndCount.Item2;
                        }
                    }

                    if (nBatches == 0)
                    {
                        AMessage = String.Format(Catalog.GetString("    You have made changes to {0} {1}.{2}"),
                            nTransactions,
                            Catalog.GetPluralString("transaction", "transactions", nTransactions),
                            Environment.NewLine);
                    }
                    else
                    {
                        AMessage = String.Format(Catalog.GetString("    You have made changes to {0} {1} and {2} {3}.{4}"),
                            nBatches,
                            Catalog.GetPluralString("batch", "batches", nBatches),
                            nTransactions,
                            Catalog.GetPluralString("transaction", "transactions", nTransactions),
                            Environment.NewLine);
                    }
                }

                AMessage += String.Format(TFrmPetraEditUtils.StrConsequenceIfNotSaved, Environment.NewLine);
            }

            return allChangesCount;
        }

        /// <summary>
        /// Check if batch columns have actually changed
        /// </summary>
        /// <param name="ARecurringBatchRow"></param>
        /// <returns></returns>
        public bool BatchColumnsHaveChanged(ARecurringGiftBatchRow ARecurringBatchRow)
        {
            bool RetVal = false;

            if (ARecurringBatchRow.RowState != DataRowState.Unchanged)
            {
                bool columnValueChanged = false;

                for (int i = 0; i < FMainDS.ARecurringGiftBatch.Columns.Count; i++)
                {
                    string originalValue = ARecurringBatchRow[i, DataRowVersion.Original].ToString();
                    string currentValue = ARecurringBatchRow[i, DataRowVersion.Current].ToString();

                    if (originalValue != currentValue)
                    {
                        columnValueChanged = true;
                        break;
                    }
                }

                if (!columnValueChanged)
                {
                    ARecurringBatchRow.RejectChanges();
                }

                RetVal = columnValueChanged;
            }

            return RetVal;
        }

        #region Forms Messaging Interface Implementation

        /// <summary>
        /// Will be called by TFormsList to inform any Form that is registered in TFormsList
        /// about any 'Forms Messages' that are broadcasted.
        /// </summary>
        /// <remarks>The Partner Edit 'listens' to such 'Forms Message' broadcasts by
        /// implementing this virtual Method. This Method will be called each time a
        /// 'Forms Message' broadcast occurs.
        /// </remarks>
        /// <param name="AFormsMessage">An instance of a 'Forms Message'. This can be
        /// inspected for parameters in the Method Body and the Form can use those to choose
        /// to react on the Message, or not.</param>
        /// <returns>Returns True if the Form reacted on the specific Forms Message,
        /// otherwise false.</returns>
        public bool ProcessFormsMessage(TFormsMessage AFormsMessage)
        {
            bool MessageProcessed = false;

            // update gift destination
            if (AFormsMessage.MessageClass == TFormsMessageClassEnum.mcGiftDestinationChanged)
            {
                ucoRecurringTransactions.ProcessGiftDetainationBroadcastMessage(AFormsMessage);

                MessageProcessed = true;
            }
            else if (AFormsMessage.MessageClass == TFormsMessageClassEnum.mcUnitHierarchyChanged)
            {
                ucoRecurringTransactions.ProcessUnitHierarchyBroadcastMessage(AFormsMessage);

                MessageProcessed = true;
            }

            return MessageProcessed;
        }

        #endregion
    }
}