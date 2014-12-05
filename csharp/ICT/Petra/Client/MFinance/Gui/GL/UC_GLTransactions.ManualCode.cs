//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop
//
// Copyright 2004-2013 by OM International
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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

using GNU.Gettext;

using Ict.Common;
using Ict.Common.Controls;
using Ict.Common.Data;
using Ict.Common.Verification;

using Ict.Petra.Client.App.Core;
using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.MFinance.Logic;

using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.GL.Data;
using Ict.Petra.Shared.MFinance.Validation;

using SourceGrid;

namespace Ict.Petra.Client.MFinance.Gui.GL
{
    public partial class TUC_GLTransactions
    {
        private bool FLoadCompleted = false;
        private Int32 FLedgerNumber = -1;
        private Int32 FBatchNumber = -1;
        private Int32 FJournalNumber = -1;
        private Int32 FTransactionNumber = -1;
        private bool FActiveOnly = true;
        private string FTransactionCurrency = string.Empty;

        private decimal FDebitAmount = 0;
        private decimal FCreditAmount = 0;

        private ABatchRow FBatchRow = null;
        private ACostCentreTable FCostCentreTable = null;
        private AAccountTable FAccountTable = null;

        private GLSetupTDS FCacheDS = null;
        private GLBatchTDSAJournalRow FJournalRow = null;
        private ATransAnalAttribRow FPSAttributesRow = null;
        private TAnalysisAttributes FAnalysisAttributesLogic;

        private SourceGrid.Cells.Editors.ComboBox FcmbAnalAttribValues;

        private bool FDoneComboInitialise = false;
        private bool FIsUnposted = true;
        private string FBatchStatus = string.Empty;
        private string FJournalStatus = string.Empty;

        private void InitialiseControls()
        {
            cmbDetailKeyMinistryKey.ComboBoxWidth = txtDetailNarrative.Width;
        }

        /// <summary>
        /// WorkAroundInitialization
        /// </summary>
        public void WorkAroundInitialization()
        {
            txtCreditAmount.Validated += new EventHandler(ControlHasChanged);
            txtDebitAmount.Validated += new EventHandler(ControlHasChanged);
            cmbDetailCostCentreCode.Validated += new EventHandler(ControlValidatedHandler);
            cmbDetailAccountCode.Validated += new EventHandler(ControlValidatedHandler);
            cmbDetailKeyMinistryKey.Validated += new EventHandler(ControlValidatedHandler);
            txtDetailNarrative.Validated += new EventHandler(ControlValidatedHandler);
            txtDetailReference.Validated += new EventHandler(ControlValidatedHandler);
            dtpDetailTransactionDate.Validated += new EventHandler(ControlValidatedHandler);
            grdAnalAttributes.Selection.SelectionChanged += new RangeRegionChangedEventHandler(AnalysisAttributesGrid_RowSelected);

            //Disallow the entry of the minus sign as no negative amounts allowed.
            //Instead, the user is expected to follow accounting riles and apply a positive amount
            //  to debit or credit accordingly to achieve the same effect
            txtDebitAmount.NegativeValueAllowed = false;
            txtCreditAmount.NegativeValueAllowed = false;
        }

        /// <summary>
        /// load the transactions into the grid
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AJournalNumber"></param>
        /// <param name="ACurrencyCode"></param>
        /// <param name="ABatchStatus"></param>
        /// <param name="AJournalStatus"></param>
        /// <param name="AFromBatchTab"></param>
        /// <returns>True if new GL transactions were loaded, false if transactions had been loaded already.</returns>
        public bool LoadTransactions(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            Int32 AJournalNumber,
            string ACurrencyCode,
            string ABatchStatus = MFinanceConstants.BATCH_UNPOSTED,
            string AJournalStatus = MFinanceConstants.BATCH_UNPOSTED,
            bool AFromBatchTab = false)
        {
            bool DifferentBatchSelected = false;

            FLoadCompleted = false;
            FBatchRow = GetBatchRow();
            FJournalRow = GetJournalRow();
            FIsUnposted = (FBatchRow.BatchStatus == MFinanceConstants.BATCH_UNPOSTED);

            if (FLedgerNumber == -1)
            {
                InitialiseControls();
            }

            //Check if the same batch is selected, so no need to apply filter
            if ((FLedgerNumber == ALedgerNumber) && (FBatchNumber == ABatchNumber) && (FJournalNumber == AJournalNumber)
                && (FTransactionCurrency == ACurrencyCode) && (FBatchStatus == ABatchStatus) && (FJournalStatus == AJournalStatus)
                && (FMainDS.ATransaction.DefaultView.Count > 0))
            {
                //Same as previously selected
                if (FIsUnposted && (GetSelectedRowIndex() > 0))
                {
                    if (AFromBatchTab)
                    {
                        SelectRowInGrid(GetSelectedRowIndex());
                    }
                    else
                    {
                        GetDetailsFromControls(GetSelectedDetailRow());
                    }
                }

                FLoadCompleted = true;
            }
            else
            {
                // A new ledger/batch
                DifferentBatchSelected = true;
                bool requireControlSetup = (FLedgerNumber == -1) || (FTransactionCurrency != ACurrencyCode);

                FLedgerNumber = ALedgerNumber;
                FBatchNumber = ABatchNumber;
                FJournalNumber = AJournalNumber;
                FTransactionNumber = -1;
                FTransactionCurrency = ACurrencyCode;

                FPreviouslySelectedDetailRow = null;
                grdDetails.SuspendLayout();
                //Empty grids before filling them
                grdDetails.DataSource = null;
                grdAnalAttributes.DataSource = null;

                FBatchStatus = ABatchStatus;
                FJournalStatus = AJournalStatus;


                // This sets the main part of the filter but excluding the additional items set by the user GUI
                // It gets the right sort order
                SetTransactionDefaultView();

                //Load from server if necessary
                if (FMainDS.ATransaction.DefaultView.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransactionATransAnalAttrib(ALedgerNumber, ABatchNumber, AJournalNumber));
                }

                // We need to call this because we have not called ShowData(), which would have set it.  This differs from the Gift screen.
                grdDetails.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ATransaction.DefaultView);

                // Now we set the full filter
                FFilterAndFindObject.ApplyFilter();

                if (grdAnalAttributes.Columns.Count == 1)
                {
                    FcmbAnalAttribValues = new SourceGrid.Cells.Editors.ComboBox(typeof(string));
                    FcmbAnalAttribValues.EnableEdit = true;
                    FcmbAnalAttribValues.EditableMode = EditableMode.Focus;
                    grdAnalAttributes.AddTextColumn(Catalog.GetString("Value"),
                        FMainDS.ATransAnalAttrib.Columns[ATransAnalAttribTable.GetAnalysisAttributeValueDBName()], 150,
                        FcmbAnalAttribValues);
                    FcmbAnalAttribValues.Control.SelectedValueChanged += new EventHandler(this.AnalysisAttributeValueChanged);

                    grdAnalAttributes.Columns[0].Width = 99;
                }

                FAnalysisAttributesLogic = new TAnalysisAttributes(FLedgerNumber, FBatchNumber, FJournalNumber);

                FAnalysisAttributesLogic.SetTransAnalAttributeDefaultView(FMainDS, FActiveOnly);
                FMainDS.ATransAnalAttrib.DefaultView.AllowNew = false;
                grdAnalAttributes.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ATransAnalAttrib.DefaultView);
                grdAnalAttributes.SetHeaderTooltip(0, Catalog.GetString("Type"));
                grdAnalAttributes.SetHeaderTooltip(1, Catalog.GetString("Value"));

                // if this form is readonly or batch is posted, then we need all account and cost centre codes, because old codes might have been used
                bool ActiveOnly = (this.Enabled && FIsUnposted);

                if (requireControlSetup || (FActiveOnly != ActiveOnly))
                {
                    FActiveOnly = ActiveOnly;

                    //Load all analysis attribute values
                    if (FCacheDS == null)
                    {
                        FCacheDS = TRemote.MFinance.GL.WebConnectors.LoadAAnalysisAttributes(FLedgerNumber, FActiveOnly);
                    }

                    SetupExtraGridFunctionality();

                    TFinanceControls.InitialiseAccountList(ref cmbDetailAccountCode, FLedgerNumber,
                        true, false, ActiveOnly, false, ACurrencyCode, true);
                    TFinanceControls.InitialiseCostCentreList(ref cmbDetailCostCentreCode, FLedgerNumber, true, false, ActiveOnly, false);
                }

                UpdateTransactionTotals();
                grdDetails.ResumeLayout();
                FLoadCompleted = true;
            }

            ShowData();
            SelectRowInGrid(1);
            ShowDetails(); //Needed because of how currency is handled

            UpdateChangeableStatus();
            UpdateRecordNumberDisplay();
            FFilterAndFindObject.SetRecordNumberDisplayProperties();

            return DifferentBatchSelected;
        }

        private void SetTransactionDefaultView(bool AAscendingOrder = true)
        {
            string sort = AAscendingOrder ? "ASC" : "DESC";

            if (FBatchNumber != -1)
            {
                string rowFilter = String.Format("{0}={1} AND {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    FJournalNumber);

                FMainDS.ATransaction.DefaultView.RowFilter = rowFilter;
                FFilterAndFindObject.FilterPanelControls.SetBaseFilter(rowFilter, true);
                FFilterAndFindObject.CurrentActiveFilter = rowFilter;
                // We don't apply the filter yet!

                FMainDS.ATransaction.DefaultView.Sort = String.Format("{0} " + sort,
                    ATransactionTable.GetTransactionNumberDBName());
            }
        }

        private ABatchRow GetBatchRow()
        {
            return ((TFrmGLBatch)ParentForm).GetBatchControl().GetSelectedDetailRow();
        }

        /// <summary>
        /// get the details of the current journal
        /// </summary>
        /// <returns></returns>
        private GLBatchTDSAJournalRow GetJournalRow()
        {
            return ((TFrmGLBatch)ParentForm).GetJournalsControl().GetSelectedDetailRow();
        }

        /// <summary>
        /// add a new transactions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void NewRow(System.Object sender, EventArgs e)
        {
            if (CreateNewATransaction())
            {
                pnlTransAnalysisAttributes.Enabled = true;
                btnDeleteAll.Enabled = btnDelete.Enabled && (FFilterAndFindObject.IsActiveFilterEqualToBase);

                //Needs to be called at end of addition process to process Analysis Attributes
                AccountCodeDetailChanged(null, null);
            }
        }

        /// <summary>
        /// make sure the correct transaction number is assigned and the journal.lastTransactionNumber is updated;
        /// will use the currently selected journal
        /// </summary>
        public void NewRowManual(ref GLBatchTDSATransactionRow ANewRow)
        {
            NewRowManual(ref ANewRow, null);
        }

        /// <summary>
        /// make sure the correct transaction number is assigned and the journal.lastTransactionNumber is updated
        /// </summary>
        /// <param name="ANewRow">returns the modified new transaction row</param>
        /// <param name="ARefJournalRow">this can be null; otherwise this is the journal that the transaction should belong to</param>
        public void NewRowManual(ref GLBatchTDSATransactionRow ANewRow, AJournalRow ARefJournalRow)
        {
            if (ARefJournalRow == null)
            {
                ARefJournalRow = FJournalRow;
            }

            ANewRow.LedgerNumber = ARefJournalRow.LedgerNumber;
            ANewRow.BatchNumber = ARefJournalRow.BatchNumber;
            ANewRow.JournalNumber = ARefJournalRow.JournalNumber;
            ANewRow.TransactionNumber = ++ARefJournalRow.LastTransactionNumber;
            ANewRow.TransactionDate = GetBatchRow().DateEffective;

            if (FPreviouslySelectedDetailRow != null)
            {
                ANewRow.CostCentreCode = FPreviouslySelectedDetailRow.CostCentreCode;
                ANewRow.Narrative = FPreviouslySelectedDetailRow.Narrative;
                ANewRow.Reference = FPreviouslySelectedDetailRow.Reference;
            }
        }

        /// <summary>
        /// show ledger, batch and journal number
        /// </summary>
        private void ShowDataManual()
        {
            if (FLedgerNumber != -1)
            {
                string TransactionCurrency = FJournalRow.TransactionCurrency;
                string BaseCurrency = FMainDS.ALedger[0].BaseCurrency;

                txtLedgerNumber.Text = TFinanceControls.GetLedgerNumberAndName(FLedgerNumber);
                txtBatchNumber.Text = FBatchRow.BatchNumber.ToString();
                txtJournalNumber.Text = FJournalNumber.ToString();

                lblTransactionCurrency.Text = String.Format(Catalog.GetString("{0} (Transaction Currency)"), TransactionCurrency);
                txtDebitAmount.CurrencyCode = TransactionCurrency;
                txtCreditAmount.CurrencyCode = TransactionCurrency;
                txtCreditTotalAmount.CurrencyCode = TransactionCurrency;
                txtDebitTotalAmount.CurrencyCode = TransactionCurrency;

                lblBaseCurrency.Text = String.Format(Catalog.GetString("{0} (Base Currency)"), BaseCurrency);
                txtDebitAmountBase.CurrencyCode = BaseCurrency;
                txtCreditAmountBase.CurrencyCode = BaseCurrency;
                txtCreditTotalAmountBase.CurrencyCode = BaseCurrency;
                txtDebitTotalAmountBase.CurrencyCode = BaseCurrency;

                // foreign currency accounts only get transactions in that currency
                if (FTransactionCurrency != TransactionCurrency)
                {
                    string SelectedAccount = cmbDetailAccountCode.GetSelectedString();

                    // if this form is readonly, then we need all account and cost centre codes, because old codes might have been used
                    bool ActiveOnly = this.Enabled;

                    TFinanceControls.InitialiseAccountList(ref cmbDetailAccountCode, FLedgerNumber,
                        true, false, ActiveOnly, false, TransactionCurrency);

                    cmbDetailAccountCode.SetSelectedString(SelectedAccount);

                    FTransactionCurrency = TransactionCurrency;

                    if (!FIsUnposted && FPetraUtilsObject.HasChanges)
                    {
                        FPetraUtilsObject.DisableSaveButton();
                    }
                }

                // Needs to be called to process Analysis Attributes
                // AlanP: Not sure we need this? It already gets called.
                //AccountCodeDetailChanged(null, null);
            }
        }

        private void ShowDetailsManual(ATransactionRow ARow)
        {
            grdDetails.TabStop = (ARow != null);
            grdAnalAttributes.Enabled = (ARow != null);

            if (ARow == null)
            {
                FTransactionNumber = -1;
                ClearControls();
                btnNew.Focus();
                return;
            }

            FTransactionNumber = ARow.TransactionNumber;

            if (ARow.DebitCreditIndicator)
            {
                txtDebitAmount.NumberValueDecimal = ARow.TransactionAmount;
                txtCreditAmount.NumberValueDecimal = 0;
                txtDebitAmountBase.NumberValueDecimal = ARow.AmountInBaseCurrency;
                txtCreditAmountBase.NumberValueDecimal = 0;
            }
            else
            {
                txtDebitAmount.NumberValueDecimal = 0;
                txtCreditAmount.NumberValueDecimal = ARow.TransactionAmount;
                txtDebitAmountBase.NumberValueDecimal = 0;
                txtCreditAmountBase.NumberValueDecimal = ARow.AmountInBaseCurrency;
            }

            if (FPetraUtilsObject.HasChanges && !FIsUnposted)
            {
                FPetraUtilsObject.HasChanges = false;
                FPetraUtilsObject.DisableSaveButton();
            }

            RefreshAnalysisAttributesGrid();
        }

        private void RefreshAnalysisAttributesGrid()
        {
            //Empty the grid
            FMainDS.ATransAnalAttrib.DefaultView.RowFilter = "1=2";
            FPSAttributesRow = null;

            if ((FPreviouslySelectedDetailRow == null)
                || !pnlTransAnalysisAttributes.Enabled
                || !TRemote.MFinance.Setup.WebConnectors.AccountHasAnalysisAttributes(FLedgerNumber, cmbDetailAccountCode.GetSelectedString(),
                    FActiveOnly))
            {
                if (grdAnalAttributes.Enabled)
                {
                    grdAnalAttributes.Enabled = false;
                }

                return;
            }
            else
            {
                if (!grdAnalAttributes.Enabled)
                {
                    grdAnalAttributes.Enabled = true;
                }
            }

            FAnalysisAttributesLogic.SetTransAnalAttributeDefaultView(FMainDS, FActiveOnly, FTransactionNumber);

            grdAnalAttributes.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ATransAnalAttrib.DefaultView);

            if (grdAnalAttributes.Rows.Count > 1)
            {
                grdAnalAttributes.SelectRowWithoutFocus(1);
                AnalysisAttributesGrid_RowSelected(null, null);
            }
        }

        private void AnalysisAttributesGrid_RowSelected(System.Object sender, RangeRegionChangedEventArgs e)
        {
            if (grdAnalAttributes.Selection.ActivePosition.IsEmpty() || (grdAnalAttributes.Selection.ActivePosition.Column == 0))
            {
                return;
            }

            if ((TAnalysisAttributes.GetSelectedAttributeRow(grdAnalAttributes) == null)
                || (FPSAttributesRow == TAnalysisAttributes.GetSelectedAttributeRow(grdAnalAttributes)))
            {
                return;
            }

            FPSAttributesRow = TAnalysisAttributes.GetSelectedAttributeRow(grdAnalAttributes);

            string currentAnalTypeCode = FPSAttributesRow.AnalysisTypeCode;

            if (FIsUnposted)
            {
                FCacheDS.AFreeformAnalysis.DefaultView.RowFilter = String.Format("{0}='{1}' AND {2}=true",
                    AFreeformAnalysisTable.GetAnalysisTypeCodeDBName(),
                    currentAnalTypeCode,
                    AFreeformAnalysisTable.GetActiveDBName());
            }
            else
            {
                FCacheDS.AFreeformAnalysis.DefaultView.RowFilter = String.Format("{0}='{1}'",
                    AFreeformAnalysisTable.GetAnalysisTypeCodeDBName(),
                    currentAnalTypeCode);
            }

            int analTypeCodeValuesCount = FCacheDS.AFreeformAnalysis.DefaultView.Count;

            if (analTypeCodeValuesCount == 0)
            {
                MessageBox.Show(Catalog.GetString(
                        "No attribute values are defined!"), currentAnalTypeCode, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            string[] analTypeValues = new string[analTypeCodeValuesCount];

            FCacheDS.AFreeformAnalysis.DefaultView.Sort = AFreeformAnalysisTable.GetAnalysisValueDBName();
            int counter = 0;

            foreach (DataRowView dvr in FCacheDS.AFreeformAnalysis.DefaultView)
            {
                AFreeformAnalysisRow faRow = (AFreeformAnalysisRow)dvr.Row;
                analTypeValues[counter] = faRow.AnalysisValue;

                counter++;
            }

            //Refresh the combo values
            FcmbAnalAttribValues.StandardValuesExclusive = true;
            FcmbAnalAttribValues.StandardValues = analTypeValues;
        }

        private void AnalysisAttributeValueChanged(System.Object sender, EventArgs e)
        {
            if (!FIsUnposted)
            {
                return;
            }

            DevAge.Windows.Forms.DevAgeComboBox valueType = (DevAge.Windows.Forms.DevAgeComboBox)sender;

            int selectedValueIndex = valueType.SelectedIndex;

            if (selectedValueIndex < 0)
            {
                return;
            }
            else if (valueType.Items[selectedValueIndex].ToString() != FPSAttributesRow.AnalysisAttributeValue.ToString())
            {
                FPetraUtilsObject.SetChangedFlag();
            }
        }

        private void GetDetailDataFromControlsManual(ATransactionRow ARow)
        {
            if (ARow == null)
            {
                return;
            }

            Decimal OldTransactionAmount = ARow.TransactionAmount;
            bool OldDebitCreditIndicator = ARow.DebitCreditIndicator;

            if (txtDebitAmount.Text.Length == 0)
            {
                txtDebitAmount.NumberValueDecimal = 0;
            }

            if (txtCreditAmount.Text.Length == 0)
            {
                txtCreditAmount.NumberValueDecimal = 0;
            }

            ARow.DebitCreditIndicator = (txtDebitAmount.NumberValueDecimal.Value > 0);

            if (ARow.DebitCreditIndicator)
            {
                ARow.TransactionAmount = Math.Abs(txtDebitAmount.NumberValueDecimal.Value);

                if (txtCreditAmount.NumberValueDecimal.Value != 0)
                {
                    txtCreditAmount.NumberValueDecimal = 0;
                }
            }
            else
            {
                ARow.TransactionAmount = Math.Abs(txtCreditAmount.NumberValueDecimal.Value);
            }

            if ((OldTransactionAmount != Convert.ToDecimal(ARow.TransactionAmount))
                || (OldDebitCreditIndicator != ARow.DebitCreditIndicator))
            {
                UpdateTransactionTotals();
            }

            // If combobox to set analysis attribute value has focus when save button is pressed then currently
            // displayed value is not stored in database.
            // --> move focus to different field so that grid accepts value for storing in database
            if (FcmbAnalAttribValues.Control.Focused)
            {
                cmbDetailCostCentreCode.Focus();
            }
        }

        /// <summary>
        ///  update amount in other currencies (optional) and recalculate all totals for current batch and journal
        /// </summary>
        /// <param name="AUpdateLevel"></param>
        /// <param name="AUpdateTransDates"></param>
        public void UpdateTransactionTotals(string AUpdateLevel = "TRANSACTION", bool AUpdateTransDates = false)
        {
            bool TransactionRowActive = false;
            bool AmountsChanged = false;
            int CurrentTransBatchNumber = 0;
            int CurrentTransJournalNumber = 0;
            int CurrentTransNumber = 0;

            decimal AmtCreditTotal = 0.0M;
            decimal AmtDebitTotal = 0.0M;
            decimal AmtCreditTotalBase = 0.0M;
            decimal AmtDebitTotalBase = 0.0M;
            decimal AmtInBaseCurrency = 0.0M;
            decimal AmtInIntlCurrency = 0.0M;

            string LedgerBaseCurrency = FMainDS.ALedger[0].BaseCurrency;
            string LedgerIntlCurrency = FMainDS.ALedger[0].IntlCurrency;
            decimal IntlRateToBaseCurrency = 0;
            bool IsTransactionInIntlCurrency = false;

            int LedgerNumber;
            int CurrentBatchNumber;
            int CurrentJournalNumber;

            ABatchRow CurrentBatchRow = GetBatchRow();
            AJournalRow CurrentJournalRow = null;

            DataView JournalsToUpdateDV = new DataView(FMainDS.AJournal);
            DataView TransactionsToUpdateDV = new DataView(FMainDS.ATransaction);

            bool BatchLevelUpdate = (AUpdateLevel.ToUpper() == "BATCH");
            bool JournalLevelUpdate = (AUpdateLevel.ToUpper() == "JOURNAL");
            bool TransLevelUpdate = (AUpdateLevel.ToUpper() == "TRANSACTION");

            if (!(BatchLevelUpdate || JournalLevelUpdate || TransLevelUpdate))
            {
                TLogging.Log("UC_GLTransactions-UpdateTransactionAmounts() called with wrong first argument");
                return;
            }

            if ((CurrentBatchRow == null)
                || (CurrentBatchRow.BatchStatus != MFinanceConstants.BATCH_UNPOSTED))
            {
                return;
            }

            LedgerNumber = CurrentBatchRow.LedgerNumber;
            CurrentBatchNumber = CurrentBatchRow.BatchNumber;

            //If called at the batch level, clear the current selections
            if (BatchLevelUpdate)
            {
                CurrentJournalNumber = 0;

                FPetraUtilsObject.SuppressChangeDetection = true;
                ClearCurrentSelection();
                ((TFrmGLBatch) this.ParentForm).GetJournalsControl().ClearCurrentSelection();
                FPetraUtilsObject.SuppressChangeDetection = false;
                //Ensure that when the Journal and Trans tab is opened, the data is reloaded.
                FBatchNumber = -1;
                ((TFrmGLBatch) this.ParentForm).GetJournalsControl().FBatchNumber = -1;
            }
            else
            {
                CurrentJournalRow = GetJournalRow();
                CurrentJournalNumber = CurrentJournalRow.JournalNumber;
            }

            if (JournalLevelUpdate)
            {
                ClearCurrentSelection();
                //Ensure that when the Trans tab is opened, the data is reloaded.
                FBatchNumber = -1;
            }
            else if (TransLevelUpdate && (FPreviouslySelectedDetailRow != null))
            {
                TransactionRowActive = true;

                CurrentTransBatchNumber = FPreviouslySelectedDetailRow.BatchNumber;
                CurrentTransJournalNumber = FPreviouslySelectedDetailRow.JournalNumber;
                CurrentTransNumber = FPreviouslySelectedDetailRow.TransactionNumber;
            }

            //Get the corporate exchange rate
            IntlRateToBaseCurrency = ((TFrmGLBatch) this.ParentForm).GetInternationalCurrencyExchangeRate();

            if (!EnsureGLDataPresent(LedgerNumber, CurrentBatchNumber, CurrentJournalNumber, ref JournalsToUpdateDV, TransactionRowActive))
            {
                //No transactions exist to process or corporate exchange rate not found
                return;
            }

            //Iterate through journals
            foreach (DataRowView drv in JournalsToUpdateDV)
            {
                GLBatchTDSAJournalRow jr = (GLBatchTDSAJournalRow)drv.Row;

                IsTransactionInIntlCurrency = (jr.TransactionCurrency == LedgerIntlCurrency);

                if (BatchLevelUpdate)
                {
                    //Journal row is active
                    jr.DateEffective = CurrentBatchRow.DateEffective;
                    AmountsChanged = true;
                }

                TransactionsToUpdateDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    jr.BatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    jr.JournalNumber);

                //If all rows deleted
                if (TransactionsToUpdateDV.Count == 0)
                {
                    txtCreditAmountBase.NumberValueDecimal = 0;
                    txtCreditAmount.NumberValueDecimal = 0;
                    txtDebitAmountBase.NumberValueDecimal = 0;
                    txtDebitAmount.NumberValueDecimal = 0;
                    txtCreditTotalAmount.NumberValueDecimal = 0;
                    txtDebitTotalAmount.NumberValueDecimal = 0;
                    txtCreditTotalAmountBase.NumberValueDecimal = 0;
                    txtDebitTotalAmountBase.NumberValueDecimal = 0;
                }

                //Iterate thuogh all trnsactions in Journal
                foreach (DataRowView trv in TransactionsToUpdateDV)
                {
                    ATransactionRow tr = (ATransactionRow)trv.Row;

                    bool IsCurrentActiveTransRow = (TransactionRowActive
                                                    && tr.TransactionNumber == CurrentTransNumber
                                                    && tr.BatchNumber == CurrentTransBatchNumber
                                                    && tr.JournalNumber == CurrentTransJournalNumber);

                    if (AUpdateTransDates)
                    {
                        tr.TransactionDate = CurrentBatchRow.DateEffective;

                        AmountsChanged = true;
                    }

                    if (IsCurrentActiveTransRow)
                    {
                        if (FPreviouslySelectedDetailRow.DebitCreditIndicator)
                        {
                            if ((txtCreditAmountBase.NumberValueDecimal != 0)
                                || (txtCreditAmount.NumberValueDecimal != 0)
                                || (FPreviouslySelectedDetailRow.TransactionAmount != Convert.ToDecimal(txtDebitAmount.NumberValueDecimal)))
                            {
                                txtCreditAmountBase.NumberValueDecimal = 0;
                                txtCreditAmount.NumberValueDecimal = 0;
                                FPreviouslySelectedDetailRow.TransactionAmount = Convert.ToDecimal(txtDebitAmount.NumberValueDecimal);

                                AmountsChanged = true;
                            }
                        }
                        else
                        {
                            if ((txtDebitAmountBase.NumberValueDecimal != 0)
                                || (txtDebitAmount.NumberValueDecimal != 0)
                                || (FPreviouslySelectedDetailRow.TransactionAmount != Convert.ToDecimal(txtCreditAmount.NumberValueDecimal)))
                            {
                                txtDebitAmountBase.NumberValueDecimal = 0;
                                txtDebitAmount.NumberValueDecimal = 0;
                                FPreviouslySelectedDetailRow.TransactionAmount = Convert.ToDecimal(txtCreditAmount.NumberValueDecimal);

                                AmountsChanged = true;
                            }
                        }
                    }

                    // recalculate the amount in base currency
                    if (jr.TransactionTypeCode != CommonAccountingTransactionTypesEnum.REVAL.ToString())
                    {
                        AmtInBaseCurrency = GLRoutines.Divide(tr.TransactionAmount, jr.ExchangeRateToBase);

                        if (AmtInBaseCurrency != tr.AmountInBaseCurrency)
                        {
                            tr.AmountInBaseCurrency = AmtInBaseCurrency;

                            AmountsChanged = true;
                        }

                        if (IsTransactionInIntlCurrency)
                        {
                            if (tr.AmountInIntlCurrency != tr.TransactionAmount)
                            {
                                tr.AmountInIntlCurrency = tr.TransactionAmount;

                                AmountsChanged = true;
                            }
                        }
                        else
                        {
                            // TODO: Instead of hard coding the number of decimals to 2 (for US cent) it should come from the database.
                            AmtInIntlCurrency = (IntlRateToBaseCurrency == 0) ? 0 : GLRoutines.Divide(tr.AmountInBaseCurrency,
                                IntlRateToBaseCurrency,
                                2);

                            if (tr.AmountInIntlCurrency != AmtInIntlCurrency)
                            {
                                tr.AmountInIntlCurrency = AmtInIntlCurrency;

                                AmountsChanged = true;
                            }
                        }
                    }

                    if (tr.DebitCreditIndicator)
                    {
                        AmtDebitTotal += tr.TransactionAmount;
                        AmtDebitTotalBase += tr.AmountInBaseCurrency;

                        if (IsCurrentActiveTransRow)
                        {
                            if ((FPreviouslySelectedDetailRow.AmountInBaseCurrency != tr.AmountInBaseCurrency)
                                || (FPreviouslySelectedDetailRow.AmountInIntlCurrency != tr.AmountInIntlCurrency)
                                || (txtDebitAmountBase.NumberValueDecimal != tr.AmountInBaseCurrency)
                                || (txtCreditAmountBase.NumberValueDecimal != 0))
                            {
                                FPreviouslySelectedDetailRow.AmountInBaseCurrency = tr.AmountInBaseCurrency;
                                FPreviouslySelectedDetailRow.AmountInIntlCurrency = tr.AmountInIntlCurrency;
                                txtDebitAmountBase.NumberValueDecimal = tr.AmountInBaseCurrency;
                                txtCreditAmountBase.NumberValueDecimal = 0;

                                AmountsChanged = true;
                            }
                        }
                    }
                    else
                    {
                        AmtCreditTotal += tr.TransactionAmount;
                        AmtCreditTotalBase += tr.AmountInBaseCurrency;

                        if (IsCurrentActiveTransRow)
                        {
                            if ((FPreviouslySelectedDetailRow.AmountInBaseCurrency != tr.AmountInBaseCurrency)
                                || (FPreviouslySelectedDetailRow.AmountInIntlCurrency != tr.AmountInIntlCurrency)
                                || (txtCreditAmountBase.NumberValueDecimal != tr.AmountInBaseCurrency)
                                || (txtDebitAmountBase.NumberValueDecimal != 0))
                            {
                                FPreviouslySelectedDetailRow.AmountInBaseCurrency = tr.AmountInBaseCurrency;
                                FPreviouslySelectedDetailRow.AmountInIntlCurrency = tr.AmountInIntlCurrency;
                                txtCreditAmountBase.NumberValueDecimal = tr.AmountInBaseCurrency;
                                txtDebitAmountBase.NumberValueDecimal = 0;

                                AmountsChanged = true;
                            }
                        }
                    }
                }

                if (TransactionRowActive
                    && (jr.BatchNumber == CurrentTransBatchNumber)
                    && (jr.JournalNumber == CurrentTransJournalNumber))
                {
                    txtCreditTotalAmount.NumberValueDecimal = AmtCreditTotal;
                    txtDebitTotalAmount.NumberValueDecimal = AmtDebitTotal;
                    txtCreditTotalAmountBase.NumberValueDecimal = AmtCreditTotalBase;
                    txtDebitTotalAmountBase.NumberValueDecimal = AmtDebitTotalBase;
                }
            }

            //Update totals of Batch
            if (FLoadCompleted)
            {
                GLRoutines.UpdateTotalsOfBatch(ref FMainDS, CurrentBatchRow);
            }
            else
            {
                //In trans loading
                txtCreditTotalAmount.NumberValueDecimal = AmtCreditTotal;
                txtDebitTotalAmount.NumberValueDecimal = AmtDebitTotal;
                txtCreditTotalAmountBase.NumberValueDecimal = AmtCreditTotalBase;
                txtDebitTotalAmountBase.NumberValueDecimal = AmtDebitTotalBase;
            }

            // refresh the currency symbols
            if (TransactionRowActive)
            {
                ShowDataManual();
            }

            if (AmountsChanged)
            {
                FPetraUtilsObject.SetChangedFlag();
            }
        }

        private void SetupExtraGridFunctionality()
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
            //Prepare grid to highlight inactive accounts/cost centres
            // Create a cell view for special conditions
            SourceGrid.Cells.Views.Cell strikeoutCell = new SourceGrid.Cells.Views.Cell();
            strikeoutCell.Font = new System.Drawing.Font(grdDetails.Font, FontStyle.Strikeout);
            //strikeoutCell.ForeColor = Color.Crimson;

            // Create a condition, apply the view when true, and assign a delegate to handle it
            SourceGrid.Conditions.ConditionView conditionAccountCodeActive = new SourceGrid.Conditions.ConditionView(strikeoutCell);
            conditionAccountCodeActive.EvaluateFunction = delegate(SourceGrid.DataGridColumn column, int gridRow, object itemRow)
            {
                DataRowView row = (DataRowView)itemRow;
                string accountCode = row[ATransactionTable.ColumnAccountCodeId].ToString();
                return !AccountIsActive(accountCode);
            };

            SourceGrid.Conditions.ConditionView conditionCostCentreCodeActive = new SourceGrid.Conditions.ConditionView(strikeoutCell);
            conditionCostCentreCodeActive.EvaluateFunction = delegate(SourceGrid.DataGridColumn column, int gridRow, object itemRow)
            {
                DataRowView row = (DataRowView)itemRow;
                string costCentreCode = row[ATransactionTable.ColumnCostCentreCodeId].ToString();
                return !CostCentreIsActive(costCentreCode);
            };

            //Add conditions to columns
            int indexOfCostCentreCodeDataColumn = 2;
            int indexOfAccountCodeDataColumn = 3;

            grdDetails.Columns[indexOfCostCentreCodeDataColumn].Conditions.Add(conditionCostCentreCodeActive);
            grdDetails.Columns[indexOfAccountCodeDataColumn].Conditions.Add(conditionAccountCodeActive);

            //Prepare Analysis attributes grid to highlight inactive analysis codes
            // Create a cell view for special conditions
            SourceGrid.Cells.Views.Cell strikeoutCell2 = new SourceGrid.Cells.Views.Cell();
            strikeoutCell2.Font = new System.Drawing.Font(grdAnalAttributes.Font, FontStyle.Strikeout);
            //strikeoutCell.ForeColor = Color.Crimson;

            // Create a condition, apply the view when true, and assign a delegate to handle it
            SourceGrid.Conditions.ConditionView conditionAnalysisCodeActive = new SourceGrid.Conditions.ConditionView(strikeoutCell2);
            conditionAnalysisCodeActive.EvaluateFunction = delegate(SourceGrid.DataGridColumn column2, int gridRow2, object itemRow2)
            {
                DataRowView row2 = (DataRowView)itemRow2;
                string analysisCode = row2[ATransAnalAttribTable.ColumnAnalysisTypeCodeId].ToString();
                return !FAnalysisAttributesLogic.AnalysisCodeIsActive(
                    cmbDetailAccountCode.GetSelectedString(), FCacheDS.AAnalysisAttribute, analysisCode);
            };

            // Create a condition, apply the view when true, and assign a delegate to handle it
            SourceGrid.Conditions.ConditionView conditionAnalysisAttributeValueActive = new SourceGrid.Conditions.ConditionView(strikeoutCell2);
            conditionAnalysisAttributeValueActive.EvaluateFunction = delegate(SourceGrid.DataGridColumn column2, int gridRow2, object itemRow2)
            {
                if (itemRow2 != null)
                {
                    DataRowView row2 = (DataRowView)itemRow2;
                    string analysisCode = row2[ATransAnalAttribTable.ColumnAnalysisTypeCodeId].ToString();
                    string analysisAttributeValue = row2[ATransAnalAttribTable.ColumnAnalysisAttributeValueId].ToString();
                    return !TAnalysisAttributes.AnalysisAttributeValueIsActive(ref FcmbAnalAttribValues,
                        FCacheDS.AFreeformAnalysis,
                        analysisCode,
                        analysisAttributeValue);
                }
                else
                {
                    return false;
                }
            };

            //Add conditions to columns
            int indexOfAnalysisCodeColumn = 0;
            int indexOfAnalysisAttributeValueColumn = 1;

            grdAnalAttributes.Columns[indexOfAnalysisCodeColumn].Conditions.Add(conditionAnalysisCodeActive);
            grdAnalAttributes.Columns[indexOfAnalysisAttributeValueColumn].Conditions.Add(conditionAnalysisAttributeValueActive);
        }

        private bool AccountIsActive(string AAccountCode = "")
        {
            bool retVal = true;

            AAccountRow currentAccountRow = null;

            //If empty, read value from combo
            if (AAccountCode == string.Empty)
            {
                if ((FAccountTable != null) && (cmbDetailAccountCode.SelectedIndex != -1) && (cmbDetailAccountCode.Count > 0)
                    && (cmbDetailAccountCode.GetSelectedString() != null))
                {
                    AAccountCode = cmbDetailAccountCode.GetSelectedString();
                }
            }

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

        private bool CostCentreIsActive(string ACostCentreCode = "")
        {
            bool retVal = true;

            ACostCentreRow currentCostCentreRow = null;

            //If empty, read value from combo
            if (ACostCentreCode == string.Empty)
            {
                if ((FCostCentreTable != null) && (cmbDetailCostCentreCode.SelectedIndex != -1) && (cmbDetailCostCentreCode.Count > 0)
                    && (cmbDetailCostCentreCode.GetSelectedString() != null))
                {
                    ACostCentreCode = cmbDetailCostCentreCode.GetSelectedString();
                }
            }

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

        private void ControlHasChanged(System.Object sender, EventArgs e)
        {
            bool NumericAmountChange = false;
            int ErrorCounter = FPetraUtilsObject.VerificationResultCollection.Count;

            if (sender.GetType() == typeof(TTxtCurrencyTextBox))
            {
                NumericAmountChange = true;
                CheckAmounts((TTxtCurrencyTextBox)sender);
            }

            ControlValidatedHandler(sender, e);

            //If no errors and amount has changed then update totals
            if (NumericAmountChange && (FPetraUtilsObject.VerificationResultCollection.Count == ErrorCounter))
            {
                UpdateTransactionTotals();
            }
        }

        private void CheckAmounts(TTxtCurrencyTextBox ATxtCurrencyTextBox)
        {
            bool debitChanged = (ATxtCurrencyTextBox.Name == "txtDebitAmount");

            if (!debitChanged && (ATxtCurrencyTextBox.Name != "txtCreditAmount"))
            {
                return;
            }
            else if ((ATxtCurrencyTextBox.NumberValueDecimal == null) || !ATxtCurrencyTextBox.NumberValueDecimal.HasValue)
            {
                ATxtCurrencyTextBox.NumberValueDecimal = 0;
            }

            decimal valDebit = txtDebitAmount.NumberValueDecimal.Value;
            decimal valCredit = txtCreditAmount.NumberValueDecimal.Value;

            //If no changes then proceed no further
            if (debitChanged && (FDebitAmount == valDebit))
            {
                return;
            }
            else if (!debitChanged && (FCreditAmount == valCredit))
            {
                return;
            }

            if (debitChanged && ((valDebit > 0) && (valCredit > 0)))
            {
                txtCreditAmount.NumberValueDecimal = 0;
            }
            else if (!debitChanged && ((valDebit > 0) && (valCredit > 0)))
            {
                txtDebitAmount.NumberValueDecimal = 0;
            }
            else if (valDebit < 0)
            {
                txtDebitAmount.NumberValueDecimal = 0;
            }
            else if (valCredit < 0)
            {
                txtCreditAmount.NumberValueDecimal = 0;
            }

            //Reset class variables
            FDebitAmount = txtDebitAmount.NumberValueDecimal.Value;
            FCreditAmount = txtCreditAmount.NumberValueDecimal.Value;
        }

        /// <summary>
        /// enable or disable the buttons
        /// </summary>
        private void UpdateChangeableStatus()
        {
            bool changeable = !FPetraUtilsObject.DetailProtectedMode
                              && (GetBatchRow() != null)
                              && (FIsUnposted)
                              && (FJournalRow.JournalStatus == MFinanceConstants.BATCH_UNPOSTED);
            bool canDeleteAll = (FFilterAndFindObject.IsActiveFilterEqualToBase);
            bool rowsInGrid = (grdDetails.Rows.Count > 1);

            // pnlDetailsProtected must be changed first: when the enabled property of the control is changed, the focus changes, which triggers validation
            pnlDetailsProtected = !changeable;
            pnlDetails.Enabled = (changeable && rowsInGrid);
            btnDelete.Enabled = (changeable && rowsInGrid);
            btnDeleteAll.Enabled = (changeable && canDeleteAll && rowsInGrid);
            pnlTransAnalysisAttributes.Enabled = changeable;
            //lblAnalAttributes.Enabled = (changeable && rowsInGrid);
            btnNew.Enabled = changeable;
        }

        private void DeleteAllTrans(System.Object sender, EventArgs e)
        {
            if ((FPreviouslySelectedDetailRow == null) || !FIsUnposted)
            {
                return;
            }

            if ((MessageBox.Show(String.Format(Catalog.GetString(
                             "You have chosen to delete all transactions in this Journal ({0}).\n\nDo you really want to continue?"),
                         FJournalNumber),
                     Catalog.GetString("Confirm Deletion"),
                     MessageBoxButtons.YesNo,
                     MessageBoxIcon.Question,
                     MessageBoxDefaultButton.Button2) == System.Windows.Forms.DialogResult.Yes))
            {
                try
                {
                    //Unbind any transactions currently being editied in the Transaction Tab
                    ClearCurrentSelection();

                    //Delete transactions
                    DataView TransDV = new DataView(FMainDS.ATransaction);
                    DataView TransAttribDV = new DataView(FMainDS.ATransAnalAttrib);

                    TransDV.AllowDelete = true;
                    TransAttribDV.AllowDelete = true;

                    TransDV.RowFilter = String.Format("{0}={1} AND {2}={3}",
                        ATransactionTable.GetBatchNumberDBName(),
                        FBatchNumber,
                        ATransactionTable.GetJournalNumberDBName(),
                        FJournalNumber);

                    TransDV.Sort = String.Format("{0} ASC",
                        ATransactionTable.GetTransactionNumberDBName());

                    TransAttribDV.RowFilter = String.Format("{0}={1} AND {2}={3}",
                        ATransactionTable.GetBatchNumberDBName(),
                        FBatchNumber,
                        ATransactionTable.GetJournalNumberDBName(),
                        FJournalNumber);

                    TransAttribDV.Sort = String.Format("{0} ASC, {1} ASC",
                        ATransAnalAttribTable.GetTransactionNumberDBName(),
                        ATransAnalAttribTable.GetAnalysisTypeCodeDBName());

                    for (int i = TransAttribDV.Count - 1; i >= 0; i--)
                    {
                        TransAttribDV.Delete(i);
                    }

                    for (int i = TransDV.Count - 1; i >= 0; i--)
                    {
                        TransDV.Delete(i);
                    }

                    //Set last journal number
                    GetJournalRow().LastTransactionNumber = 0;

                    FPetraUtilsObject.SetChangedFlag();

                    //Need to call save
                    if (((TFrmGLBatch)ParentForm).SaveChanges())
                    {
                        MessageBox.Show(Catalog.GetString("The journal has been cleared successfully!"),
                            Catalog.GetString("Success"),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);

                        UpdateTransactionTotals();
                        ((TFrmGLBatch)ParentForm).SaveChanges();
                    }
                    else
                    {
                        // saving failed, therefore do not try to post
                        MessageBox.Show(Catalog.GetString(
                                "The journal has been cleared but there were problems during saving; ") + Environment.NewLine +
                            Catalog.GetString("Please try and save immediately."),
                            Catalog.GetString("Failure"), MessageBoxButtons.OK, MessageBoxIcon.Error);

                        FMainDS.RejectChanges();
                        SelectRowInGrid(1);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    FMainDS.RejectChanges();
                }

                //If some row(s) still exist after deletion
                if (grdDetails.Rows.Count < 2)
                {
                    UpdateChangeableStatus();
                    ClearControls();
                }
            }
        }

        private bool PreDeleteManual(ATransactionRow ARowToDelete, ref string ADeletionQuestion)
        {
            bool allowDeletion = true;

            if (FPreviouslySelectedDetailRow != null)
            {
                ADeletionQuestion = String.Format(Catalog.GetString("Are you sure you want to delete transaction no. {0} from Journal {1}?"),
                    ARowToDelete.TransactionNumber,
                    ARowToDelete.JournalNumber);
            }

            return allowDeletion;
        }

        /// <summary>
        /// Code to be run after the deletion process
        /// </summary>
        /// <param name="ARowToDelete">the row that was/was to be deleted</param>
        /// <param name="AAllowDeletion">whether or not the user was permitted to delete</param>
        /// <param name="ADeletionPerformed">whether or not the deletion was performed successfully</param>
        /// <param name="ACompletionMessage">if specified, is the deletion completion message</param>
        private void PostDeleteManual(ATransactionRow ARowToDelete,
            bool AAllowDeletion,
            bool ADeletionPerformed,
            string ACompletionMessage)
        {
            if (ADeletionPerformed)
            {
                //UpdateTransactionTotals() now does this
                //((TFrmGLBatch)this.ParentForm).GetJournalsControl().SetJournalLastTransNumber(true);

                UpdateChangeableStatus();

                if (!pnlDetails.Enabled)
                {
                    ClearControls();
                }

                ((TFrmGLBatch) this.ParentForm).SaveChanges();

                UpdateTransactionTotals();

                ((TFrmGLBatch) this.ParentForm).SaveChanges();

                //message to user
                MessageBox.Show(ACompletionMessage,
                    "Deletion Successful",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else if (!AAllowDeletion && (ACompletionMessage.Length > 0))
            {
                //message to user
                MessageBox.Show(ACompletionMessage,
                    "Deletion not allowed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else if (!ADeletionPerformed && (ACompletionMessage.Length > 0))
            {
                //message to user
                MessageBox.Show(ACompletionMessage,
                    "Deletion failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Deletes the current row and optionally populates a completion message
        /// </summary>
        /// <param name="ARowToDelete">the currently selected row to delete</param>
        /// <param name="ACompletionMessage">if specified, is the deletion completion message</param>
        /// <returns>true if row deletion is successful</returns>
        private bool DeleteRowManual(ATransactionRow ARowToDelete, ref string ACompletionMessage)
        {
            //Assign a default values
            bool deletionSuccessful = false;

            ACompletionMessage = string.Empty;

            if (ARowToDelete == null)
            {
                return deletionSuccessful;
            }

            bool newRecord = (ARowToDelete.RowState == DataRowState.Added);

            if (!newRecord && !((TFrmGLBatch) this.ParentForm).SaveChanges())
            {
                MessageBox.Show("Error in trying to save prior to deleting current transaction!");
                return deletionSuccessful;
            }

            //Backup the Dataset for reversion purposes
            GLBatchTDS FTempDS = (GLBatchTDS)FMainDS.Copy();

            int transactionNumberToDelete = ARowToDelete.TransactionNumber;
            int lastTransactionNumber = FJournalRow.LastTransactionNumber;

            try
            {
                // Delete on client side data through views that is already loaded. Data that is not
                // loaded yet will be deleted with cascading delete on server side so we don't have
                // to worry about this here.

                FAnalysisAttributesLogic.SetTransAnalAttributeDefaultView(FMainDS, FActiveOnly, transactionNumberToDelete);
                DataView attrView = FMainDS.ATransAnalAttrib.DefaultView;

                if (attrView.Count > 0)
                {
                    //Iterate through attributes and delete
                    ATransAnalAttribRow attrRowCurrent = null;

                    foreach (DataRowView gv in attrView)
                    {
                        attrRowCurrent = (ATransAnalAttribRow)gv.Row;

                        attrRowCurrent.Delete();
                    }
                }

                //Reduce those with higher transaction number by one
                attrView.RowFilter = String.Format("{0} = {1} AND {2} = {3} AND {4} > {5}",
                    ATransAnalAttribTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransAnalAttribTable.GetJournalNumberDBName(),
                    FJournalNumber,
                    ATransAnalAttribTable.GetTransactionNumberDBName(),
                    transactionNumberToDelete);

                // Delete the associated transaction analysis attributes
                //  if attributes do exist, and renumber those above
                if (attrView.Count > 0)
                {
                    //Iterate through higher number attributes and transaction numbers and reduce by one
                    ATransAnalAttribRow attrRowCurrent = null;

                    foreach (DataRowView gv in attrView)
                    {
                        attrRowCurrent = (ATransAnalAttribRow)gv.Row;

                        attrRowCurrent.TransactionNumber--;
                    }
                }

                //Bubble the transaction to delete to the top
                DataView transView = new DataView(FMainDS.ATransaction);
                transView.RowFilter = String.Format("{0}={1} AND {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    FJournalNumber);

                transView.Sort = String.Format("{0} ASC",
                    ATransactionTable.GetTransactionNumberDBName());

                ATransactionRow transRowToReceive = null;
                ATransactionRow transRowToCopyDown = null;
                ATransactionRow transRowCurrent = null;

                int currentTransNo = 0;

                foreach (DataRowView gv in transView)
                {
                    transRowCurrent = (ATransactionRow)gv.Row;

                    currentTransNo = transRowCurrent.TransactionNumber;

                    if (currentTransNo > transactionNumberToDelete)
                    {
                        transRowToCopyDown = transRowCurrent;

                        //Copy column values down
                        for (int j = 4; j < transRowToCopyDown.Table.Columns.Count; j++)
                        {
                            //Update all columns except the pk fields that remain the same
                            if (!transRowToCopyDown.Table.Columns[j].ColumnName.EndsWith("_text"))
                            {
                                // Don't include the columns that the filter uses for numeric textual comparison
                                transRowToReceive[j] = transRowToCopyDown[j];
                            }
                        }
                    }

                    if (currentTransNo == transView.Count)  //Last row which is the row to be deleted
                    {
                        //Mark last record for deletion
                        transRowCurrent.SubType = MFinanceConstants.MARKED_FOR_DELETION;
                    }

                    //transRowToReceive will become previous row for next recursion
                    transRowToReceive = transRowCurrent;
                }

                if (newRecord && (transRowCurrent.SubType == MFinanceConstants.MARKED_FOR_DELETION))
                {
                    transRowCurrent.Delete();
                }

                FPreviouslySelectedDetailRow = null;

                FPetraUtilsObject.SetChangedFlag();

                //Try to save changes
                if (!newRecord)
                {
                    if (!((TFrmGLBatch) this.ParentForm).SaveChanges())
                    {
                        throw new Exception("Unable to save after deleting a transaction!");
                    }
                }

                ACompletionMessage = String.Format(Catalog.GetString("Transaction no.: {0} deleted successfully."),
                    transactionNumberToDelete);

                deletionSuccessful = true;
            }
            catch (Exception ex)
            {
                ACompletionMessage = ex.Message;
                MessageBox.Show(ex.Message,
                    "Deletion Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                //Revert to previous state
                FMainDS = (GLBatchTDS)FTempDS.Copy();
            }
            finally
            {
                SetTransactionDefaultView();
                FFilterAndFindObject.ApplyFilter();
            }

            return deletionSuccessful;
        }

        /// <summary>
        /// clear the current selection
        /// </summary>
        public void ClearCurrentSelection()
        {
            this.FPreviouslySelectedDetailRow = null;
        }

        private void ClearControls()
        {
            //Stop data change detection
            FPetraUtilsObject.DisableDataChangedEvent();

            //Clear combos
            cmbDetailAccountCode.SelectedIndex = -1;
            cmbDetailCostCentreCode.SelectedIndex = -1;
            cmbDetailKeyMinistryKey.SelectedIndex = -1;
            //Clear Textboxes
            txtDetailNarrative.Clear();
            txtDetailReference.Clear();
            //Clear Numeric Textboxes
            txtDebitAmount.NumberValueDecimal = 0;
            txtCreditAmount.NumberValueDecimal = 0;
            txtDebitAmountBase.NumberValueDecimal = 0;
            txtCreditAmountBase.NumberValueDecimal = 0;
            //Refresh grids
            RefreshAnalysisAttributesGrid();
            //Enable data change detection
            FPetraUtilsObject.EnableDataChangedEvent();
        }

        /// <summary>
        /// if the cost centre code changes
        /// </summary>
        private void CostCentreCodeDetailChanged(object sender, EventArgs e)
        {
            if ((FLoadCompleted == false) || (FPreviouslySelectedDetailRow == null)
                || (cmbDetailCostCentreCode.GetSelectedString() == String.Empty) || (cmbDetailCostCentreCode.SelectedIndex == -1))
            {
                return;
            }

            // update key ministry combobox depending on account code and cost centre
            UpdateCmbDetailKeyMinistryKey();
        }

        /// <summary>
        /// if the account code changes, analysis types/attributes  have to be updated
        /// </summary>
        private void AccountCodeDetailChanged(object sender, EventArgs e)
        {
            if (FPreviouslySelectedDetailRow == null)
            {
                return;
            }

            if ((FPreviouslySelectedDetailRow.TransactionNumber == FTransactionNumber) && (FTransactionNumber != -1))
            {
                FAnalysisAttributesLogic.ReconcileTransAnalysisAttributes(ref FMainDS, cmbDetailAccountCode.GetSelectedString(), FTransactionNumber);
                RefreshAnalysisAttributesGrid();
            }

            // update key ministry combobox depending on account code and cost centre
            UpdateCmbDetailKeyMinistryKey();
        }

        /// <summary>
        /// if the cost centre code changes
        /// </summary>
        private void UpdateCmbDetailKeyMinistryKey()
        {
            Int64 RecipientKey;

            // update key ministry combobox depending on account code and cost centre
            if ((cmbDetailAccountCode.GetSelectedString() == MFinanceConstants.FUND_TRANSFER_INCOME_ACC)
                && (cmbDetailCostCentreCode.GetSelectedString() != ""))
            {
                cmbDetailKeyMinistryKey.Enabled = true;
                TRemote.MFinance.Common.ServerLookups.WebConnectors.GetPartnerKeyForForeignCostCentreCode(FLedgerNumber,
                    cmbDetailCostCentreCode.GetSelectedString(),
                    out RecipientKey);
                TFinanceControls.GetRecipientData(ref cmbDetailKeyMinistryKey, RecipientKey);
                cmbDetailKeyMinistryKey.ComboBoxWidth = txtDetailNarrative.Width;
            }
            else
            {
                cmbDetailKeyMinistryKey.SetSelectedString("", -1);
                cmbDetailKeyMinistryKey.Enabled = false;
            }
        }

        private void ValidateDataDetailsManual(ATransactionRow ARow)
        {
            if ((ARow == null) || (GetBatchRow() == null) || !FIsUnposted)
            {
                return;
            }

            TVerificationResultCollection VerificationResultCollection = FPetraUtilsObject.VerificationResultCollection;

            Control controlToPass = null;

            //Local validation
            if (((txtDebitAmount.NumberValueDecimal.Value == 0) && (txtCreditAmount.NumberValueDecimal.Value == 0))
                || (txtDebitAmount.NumberValueDecimal.Value < 0))
            {
                controlToPass = txtDebitAmount;
            }
            else if (txtCreditAmount.NumberValueDecimal.Value < 0)
            {
                controlToPass = txtCreditAmount;
            }
            else if (TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_GLREFMANDATORY, "no") == "yes")
            {
                controlToPass = txtDetailReference;
            }

            if ((controlToPass == null) || (controlToPass == txtDetailReference))
            {
                //This is needed because the above runs many times during setting up the form
                VerificationResultCollection.Clear();
            }

            TSharedFinanceValidation_GL.ValidateGLDetailManual(this, FBatchRow, ARow, controlToPass, ref VerificationResultCollection,
                FValidationControlsDict);

            if ((FPreviouslySelectedDetailRow != null) && !FAnalysisAttributesLogic.AccountAnalysisAttributeCountIsCorrect(
                    FPreviouslySelectedDetailRow.TransactionNumber, FPreviouslySelectedDetailRow.AccountCode, FMainDS, FIsUnposted))
            {
                DataColumn ValidationColumn;
                TVerificationResult VerificationResult = null;
                object ValidationContext;

                ValidationColumn = ARow.Table.Columns[ATransactionTable.ColumnAccountCodeId];
                ValidationContext = "unused because of OverrideResultText";

                // This code is only running because of failure, so cause an error to occur.
                VerificationResult = TStringChecks.StringMustNotBeEmpty("",
                    ValidationContext.ToString(),
                    this, ValidationColumn, null);
                VerificationResult.OverrideResultText(String.Format(
                        "A value must be entered for 'Analysis Attributes' for Account Code {0} in Transaction {1}.{2}{2}" +
                        "CLICK THE DOWN ARROW NEXT TO THE ACCOUNT CODE BOX TO OPEN THE LIST AND THEN RESELECT ACCOUNT CODE {0}",
                        ARow.AccountCode,
                        ARow.TransactionNumber,
                        Environment.NewLine));

                // Handle addition/removal to/from TVerificationResultCollection
                VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn, true);
            }

            String ValueRequiredForType;

            if ((FPreviouslySelectedDetailRow != null) && !FAnalysisAttributesLogic.AccountAnalysisAttributesValuesExist(
                    FPreviouslySelectedDetailRow.TransactionNumber, FPreviouslySelectedDetailRow.AccountCode, FMainDS, out ValueRequiredForType,
                    FIsUnposted))
            {
                DataColumn ValidationColumn;
                TVerificationResult VerificationResult = null;
                object ValidationContext;

                ValidationColumn = ARow.Table.Columns[ATransactionTable.ColumnAccountCodeId];
                ValidationContext = String.Format("Analysis code {0} for Account Code {1} in Transaction {2}",
                    ValueRequiredForType,
                    ARow.AccountCode,
                    ARow.TransactionNumber);

                // This code is only running because of failure, so cause an error to occur.
                VerificationResult = TStringChecks.StringMustNotBeEmpty("",
                    ValidationContext.ToString(),
                    this, ValidationColumn, null);

                // Handle addition/removal to/from TVerificationResultCollection
                VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn, true);
            }
        }

        /// <summary>
        /// Set focus to the gid controltab
        /// </summary>
        public void FocusGrid()
        {
            if ((grdDetails != null) && grdDetails.Enabled && grdDetails.TabStop)
            {
                grdDetails.Focus();
            }
        }

        private void DebitAmountChanged(object sender, EventArgs e)
        {
            if (sender != null)
            {
                if ((txtDebitAmount.NumberValueDecimal != 0) && (txtCreditAmount.NumberValueDecimal != 0))
                {
                    txtCreditAmount.NumberValueDecimal = 0;
                }
            }
        }

        private void CreditAmountChanged(object sender, EventArgs e)
        {
            if (sender != null)
            {
                if ((txtCreditAmount.NumberValueDecimal != 0) && (txtDebitAmount.NumberValueDecimal != 0))
                {
                    txtDebitAmount.NumberValueDecimal = 0;
                }
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

        private void RunOnceOnParentActivationManual()
        {
            grdDetails.DataSource.ListChanged += new System.ComponentModel.ListChangedEventHandler(DataSource_ListChanged);
        }

        private void DataSource_ListChanged(object sender, System.ComponentModel.ListChangedEventArgs e)
        {
            if (grdDetails.CanFocus && (grdDetails.Rows.Count > 1))
            {
                grdDetails.AutoResizeGrid();
            }

            // If the grid list changes we might need to disable the Delete All button
            btnDeleteAll.Enabled = btnDelete.Enabled && (FFilterAndFindObject.IsActiveFilterEqualToBase);
        }

        private void TransDateChanged(object sender, EventArgs e)
        {
            if ((FPetraUtilsObject == null) || FPetraUtilsObject.SuppressChangeDetection || (FPreviouslySelectedDetailRow == null))
            {
                return;
            }

            try
            {
                DateTime dateValue;

                string aDate = dtpDetailTransactionDate.Date.ToString();

                if (!DateTime.TryParse(aDate, out dateValue))
                {
                    dtpDetailTransactionDate.Date = GetBatchRow().DateEffective;
                }
            }
            catch
            {
                //Do nothing
            }
        }

        /// <summary>
        /// Cancel any changes made to this form
        /// </summary>
        public void CancelChangesToFixedBatches()
        {
            if ((GetBatchRow() != null) && (GetBatchRow().BatchStatus != MFinanceConstants.BATCH_UNPOSTED))
            {
                FMainDS.ATransaction.RejectChanges();
            }
        }

        private Boolean EnsureGLDataPresent(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            Int32 AJournalNumber,
            ref DataView AJournalDV,
            bool AUpdateCurrentTransOnly)
        {
            bool RetVal = false;

            DataView TransDV = new DataView(FMainDS.ATransaction);

            if (AUpdateCurrentTransOnly)
            {
                AJournalDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    AJournalTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    AJournalTable.GetJournalNumberDBName(),
                    AJournalNumber);

                RetVal = true;
            }
            else if (AJournalNumber == 0)
            {
                AJournalDV.RowFilter = String.Format("{0}={1} And {2}='{3}'",
                    AJournalTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    AJournalTable.GetJournalStatusDBName(),
                    MFinanceConstants.BATCH_UNPOSTED);

                if (AJournalDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadAJournalATransaction(ALedgerNumber, ABatchNumber));

                    if (AJournalDV.Count == 0)
                    {
                        return false;
                    }
                }

                TransDV.RowFilter = String.Format("{0}={1}",
                    ATransactionTable.GetBatchNumberDBName(),
                    ABatchNumber);

                if (TransDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransactionForBatch(ALedgerNumber, ABatchNumber));
                }

                //As long as transactions exist, return true
                RetVal = true;
            }
            else
            {
                AJournalDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    AJournalTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    AJournalTable.GetJournalNumberDBName(),
                    AJournalNumber);

                if (AJournalDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadAJournal(ALedgerNumber, ABatchNumber, AJournalNumber));

                    if (AJournalDV.Count == 0)
                    {
                        return false;
                    }
                }

                TransDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    AJournalNumber);

                if (TransDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransaction(ALedgerNumber, ABatchNumber, AJournalNumber));
                }

                RetVal = true;
            }

            return RetVal;
        }

        private void ImportTransactionsFromFile(object sender, EventArgs e)
        {
            if (ValidateAllData(true, true))
            {
                ((TFrmGLBatch)ParentForm).GetBatchControl().ImportTransactions(TUC_GLBatches_Import.TImportDataSourceEnum.FromFile);
                // The import method refreshes the screen if the import is successful
            }
        }

        private void ImportTransactionsFromClipboard(object sender, EventArgs e)
        {
            if (ValidateAllData(true, true))
            {
                ((TFrmGLBatch)ParentForm).GetBatchControl().ImportTransactions(TUC_GLBatches_Import.TImportDataSourceEnum.FromClipboard);
                // The import method refreshes the screen if the import is successful
            }
        }

        /// <summary>
        /// Select a row in the grid
        ///   Called from outside
        /// </summary>
        /// <param name="ARowNumber"></param>
        public void SelectRow(int ARowNumber)
        {
            SelectRowInGrid(ARowNumber);
            UpdateRecordNumberDisplay();
        }

        private void FilterToggledManual(bool AFilterIsOff)
        {
            // The first time the filter is toggled on we need to set up the cost centre and account comboBoxes
            // This means showing inactive values in red
            // We achieve this by using our own owner draw mode event
            // Also the data source for the combos will be wrong because they have been cloned from items that may not have shown inactive values
            if ((AFilterIsOff == false) && !FDoneComboInitialise)
            {
                InitFilterFindComboBox((TCmbAutoComplete)FFilterAndFindObject.FilterPanelControls.FindControlByName("cmbDetailAccountCode"),
                    TCacheableFinanceTablesEnum.AccountList);
                InitFilterFindComboBox((TCmbAutoComplete)FFilterAndFindObject.FilterPanelControls.FindControlByName("cmbDetailCostCentreCode"),
                    TCacheableFinanceTablesEnum.CostCentreList);
                InitFilterFindComboBox((TCmbAutoComplete)FFilterAndFindObject.FindPanelControls.FindControlByName("cmbDetailAccountCode"),
                    TCacheableFinanceTablesEnum.AccountList);
                InitFilterFindComboBox((TCmbAutoComplete)FFilterAndFindObject.FindPanelControls.FindControlByName("cmbDetailCostCentreCode"),
                    TCacheableFinanceTablesEnum.CostCentreList);

                FDoneComboInitialise = true;
            }
        }

        /// <summary>
        /// Helper method that we can call to initialise each of the filter/find comboBoxes
        /// </summary>
        private void InitFilterFindComboBox(TCmbAutoComplete AFFInstance, TCacheableFinanceTablesEnum AListTable)
        {
            AFFInstance.DataSource = TDataCache.TMFinance.GetCacheableFinanceTable(AListTable, FLedgerNumber).DefaultView;
            AFFInstance.DrawMode = DrawMode.OwnerDrawFixed;
            AFFInstance.DrawItem += new DrawItemEventHandler(DrawComboBoxItem);
        }

        /// <summary>
        /// This method is called when the system wants to draw a comboBox item in the list.
        /// We choose the colour and weight for the font, showing inactive codes in bold red text
        /// </summary>
        private void DrawComboBoxItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            TCmbAutoComplete cmb = (TCmbAutoComplete)sender;
            DataRowView drv = (DataRowView)cmb.Items[e.Index];
            string content = drv[1].ToString();
            Brush brush;

            if (cmb.Name.StartsWith("cmbDetailCostCentre"))
            {
                brush = CostCentreIsActive(content) ? Brushes.Black : Brushes.Red;
            }
            else if (cmb.Name.StartsWith("cmbDetailAccount"))
            {
                brush = AccountIsActive(content) ? Brushes.Black : Brushes.Red;
            }
            else
            {
                throw new ArgumentException("Unexpected caller of DrawComboBoxItem event");
            }

            Font font = new Font(((Control)sender).Font, (brush == Brushes.Red) ? FontStyle.Bold : FontStyle.Regular);
            e.Graphics.DrawString(content, font, brush, new PointF(e.Bounds.X, e.Bounds.Y));
        }
    }
}