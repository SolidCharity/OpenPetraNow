//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop
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
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Specialized;
using Ict.Common.Verification;
using Ict.Common;
using Ict.Petra.Client.App.Core;
using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.CommonControls;
using Ict.Petra.Client.MFinance.Logic;
using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance.Gift.Data;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.Validation;
using Ict.Petra.Client.MReporting.Gui;
using Ict.Petra.Client.MReporting.Logic;

namespace Ict.Petra.Client.MFinance.Gui.Setup.Gift
{
    public partial class TFrmGiftMotivationSetup
    {
        private Int32 FLedgerNumber;
        private string FDescription;
        private bool FTaxDeductiblePercentageEnabled = false;

        private TCmbAutoPopulated cmbDeductibleAccountCode;

        /// <summary>
        /// maintain motivation details for this ledger
        /// </summary>
        public Int32 LedgerNumber
        {
            set
            {
                FLedgerNumber = value;

                FMainDS = TRemote.MFinance.Gift.WebConnectors.LoadMotivationDetails(FLedgerNumber);

                // to get an empty AMotivationDetailFee table, instead of null reference
                FMainDS.Merge(new GiftBatchTDS());

                TFinanceControls.InitialiseMotivationGroupList(ref cmbDetailMotivationGroupCode, FLedgerNumber, false);

                TFinanceControls.InitialiseAccountList(ref cmbDetailAccountCode, FLedgerNumber, true, false, false, false);

                // Do not include summary cost centres: we want to use one cost centre for each Motivation Details
                TFinanceControls.InitialiseCostCentreList(ref cmbDetailCostCentreCode, FLedgerNumber, true, false, false, true);

                TFinanceControls.InitialiseFeesReceivableList(ref clbDetailFeesReceivable, FLedgerNumber);
                TFinanceControls.InitialiseFeesPayableList(ref clbDetailFeesPayable, FLedgerNumber);

                DataView myDataView = FMainDS.AMotivationDetail.DefaultView;
                myDataView.AllowNew = false;
                myDataView.Sort = String.Format("{0} ASC, {1} ASC",
                    AMotivationDetailFeeTable.GetMotivationGroupCodeDBName(),
                    AMotivationDetailFeeTable.GetMotivationDetailCodeDBName()
                    );
                grdDetails.DataSource = new DevAge.ComponentModel.BoundDataView(myDataView);
                grdDetails.AutoSizeCells();

                this.Text = this.Text + "   [Ledger = " + FLedgerNumber.ToString() + "]";
                mniFilePrint.Click += FilePrint;
                mniFilePrint.Enabled = true;

                SelectRowInGrid(1);
                UpdateRecordNumberDisplay();

                // should Tax Deductibility Percentage be enabled? (specifically for OM Switzerland)
                FTaxDeductiblePercentageEnabled = Convert.ToBoolean(
                    TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_TAXDEDUCTIBLEPERCENTAGE, "FALSE"));

                if (FTaxDeductiblePercentageEnabled)
                {
                    SetupTaxDeductibilityControls();
                }
            }
        }

        /// <summary>
        /// Print out the Motivation Details using FastReports template.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FilePrint(object sender, EventArgs e)
        {
            FastReportsWrapper ReportingEngine = new FastReportsWrapper("Motivation Details");

            if (!ReportingEngine.LoadedOK)
            {
                ReportingEngine.ShowErrorPopup();
                return;
            }

            ReportingEngine.RegisterData(FMainDS.AMotivationDetail, "MotivationDetail");
            TRptCalculator Calc = new TRptCalculator();
            ALedgerRow LedgerRow = FMainDS.ALedger[0];
            Calc.AddParameter("param_ledger_number_i", LedgerRow.LedgerNumber);
            Calc.AddStringParameter("param_ledger_name", LedgerRow.LedgerName);

            if (ModifierKeys.HasFlag(Keys.Control))
            {
                ReportingEngine.DesignReport(Calc);
            }
            else
            {
                ReportingEngine.GenerateReport(Calc);
            }
        }

        private void InitializeManualCode()
        {
            // Get the current description
            FDescription = txtDetailMotivationDetailDesc.Text;
        }

        private void NewRowManual(ref AMotivationDetailRow ARow)
        {
            ARow.LedgerNumber = FLedgerNumber;

            if ((FMainDS.AMotivationGroup == null) || (FMainDS.AMotivationGroup.Rows.Count == 0))
            {
                MessageBox.Show(Catalog.GetString("You must define at least one Motivation Group."), Catalog.GetString("New Motivation Detail"),
                    MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            ARow.MotivationGroupCode = FMainDS.AMotivationGroup[0].MotivationGroupCode;

            string newName = Catalog.GetString("NEWDETAIL");
            Int32 countNewDetail = 0;

            if (FMainDS.AMotivationDetail.Rows.Find(new object[] { FLedgerNumber, ARow.MotivationGroupCode, newName }) != null)
            {
                while (FMainDS.AMotivationDetail.Rows.Find(new object[] { FLedgerNumber, ARow.MotivationGroupCode, newName +
                                                                          countNewDetail.ToString() }) != null)
                {
                    countNewDetail++;
                }

                newName += countNewDetail.ToString();
            }

            ARow.MotivationDetailCode = newName;
            ARow.MotivationDetailDesc = Catalog.GetString("PLEASE ENTER DESCRIPTION");
        }

        private TSubmitChangesResult StoreManualCode(ref GiftBatchTDS ASubmitChanges, out TVerificationResultCollection AVerificationResult)
        {
            TSubmitChangesResult Result;

            AVerificationResult = null;

            Result = TRemote.MFinance.Gift.WebConnectors.SaveMotivationDetails(ref ASubmitChanges);

            if (Result == TSubmitChangesResult.scrOK)
            {
                // needed to reorder the two checked listboxes
                SelectRowInGrid(FPrevRowChangedRow);

                // refresh cachaeble table
                TDataCache.TMFinance.RefreshCacheableFinanceTable(TCacheableFinanceTablesEnum.MotivationList, FLedgerNumber);
            }

            return Result;
        }

        private void NewRecord(Object sender, EventArgs e)
        {
            CreateNewAMotivationDetail();
        }

        private void ShowDetailsManual(AMotivationDetailRow ARow)
        {
            string FeesPayable = string.Empty;
            string FeesReceivable = string.Empty;

            if (ARow != null)
            {
                FMainDS.AMotivationDetailFee.DefaultView.RowFilter =
                    String.Format("{0}={1} and {2}='{3}' and {4}='{5}'",
                        AMotivationDetailFeeTable.GetLedgerNumberDBName(),
                        ARow.LedgerNumber,
                        AMotivationDetailFeeTable.GetMotivationGroupCodeDBName(),
                        ARow.MotivationGroupCode,
                        AMotivationDetailFeeTable.GetMotivationDetailCodeDBName(),
                        ARow.MotivationDetailCode);

                foreach (DataRowView rv in FMainDS.AMotivationDetailFee.DefaultView)
                {
                    AMotivationDetailFeeRow detailFeeRow = (AMotivationDetailFeeRow)rv.Row;

                    if (StringHelper.StrSplit(clbDetailFeesPayable.GetAllStringList(), ",").Contains(detailFeeRow.FeeCode))
                    {
                        FeesPayable = StringHelper.AddCSV(FeesPayable, detailFeeRow.FeeCode);
                    }
                    else
                    {
                        FeesReceivable = StringHelper.AddCSV(FeesReceivable, detailFeeRow.FeeCode);
                    }
                }
            }

            // set the ORDER column to true if row is checked
            clbDetailFeesPayable.CheckedColumn = "ORDER";
            clbDetailFeesReceivable.CheckedColumn = "ORDER";
            clbDetailFeesPayable.SetCheckedStringList(FeesPayable);
            clbDetailFeesReceivable.SetCheckedStringList(FeesReceivable);

            // set the CHECKED column to true if row is checked
            clbDetailFeesPayable.CheckedColumn = "CHECKED";
            clbDetailFeesReceivable.CheckedColumn = "CHECKED";
            clbDetailFeesPayable.SetCheckedStringList(FeesPayable);
            clbDetailFeesReceivable.SetCheckedStringList(FeesReceivable);

            if (FTaxDeductiblePercentageEnabled)
            {
                if (ARow.IsTaxDeductibleAccountNull())
                {
                    cmbDeductibleAccountCode.SelectedIndex = 0;
                }
                else
                {
                    cmbDeductibleAccountCode.SetSelectedString(ARow.TaxDeductibleAccount);
                }
            }
        }

        private void GetDetailDataFromControlsManual(AMotivationDetailRow ARow)
        {
            StringCollection TickedFees = StringHelper.StrSplit(
                StringHelper.ConcatCSV(clbDetailFeesPayable.GetCheckedStringList(), clbDetailFeesReceivable.GetCheckedStringList()),
                ",");

            FMainDS.AMotivationDetailFee.DefaultView.RowFilter =
                String.Format("{0}={1} and {2}='{3}' and {4}='{5}'",
                    AMotivationDetailFeeTable.GetLedgerNumberDBName(),
                    ARow.LedgerNumber,
                    AMotivationDetailFeeTable.GetMotivationGroupCodeDBName(),
                    ARow.MotivationGroupCode,
                    AMotivationDetailFeeTable.GetMotivationDetailCodeDBName(),
                    ARow.MotivationDetailCode);

            StringCollection ExistingFees = new StringCollection();

            foreach (DataRowView rv in FMainDS.AMotivationDetailFee.DefaultView)
            {
                AMotivationDetailFeeRow detailFeeRow = (AMotivationDetailFeeRow)rv.Row;

                if (!TickedFees.Contains(detailFeeRow.FeeCode))
                {
                    // delete existing fees that have been unticked
                    detailFeeRow.Delete();
                }
                else
                {
                    ExistingFees.Add(detailFeeRow.FeeCode);
                }
            }

            // add new fees
            foreach (string fee in TickedFees)
            {
                if (!ExistingFees.Contains(fee))
                {
                    AMotivationDetailFeeRow NewRow = FMainDS.AMotivationDetailFee.NewRowTyped();
                    NewRow.LedgerNumber = ARow.LedgerNumber;
                    NewRow.MotivationGroupCode = ARow.MotivationGroupCode;
                    NewRow.MotivationDetailCode = ARow.MotivationDetailCode;
                    NewRow.FeeCode = fee;
                    FMainDS.AMotivationDetailFee.Rows.Add(NewRow);
                }
            }

            if (FTaxDeductiblePercentageEnabled)
            {
                ARow.TaxDeductibleAccount = cmbDeductibleAccountCode.GetSelectedString();
            }
        }

        private bool PreDeleteManual(AMotivationDetailRow ARowToDelete, ref string ADeletionQuestion)
        {
            ADeletionQuestion = Catalog.GetString("Are you sure you want to delete the current row?");
            ADeletionQuestion += String.Format("{0}{0}({1} {2}, {3} {4})",
                Environment.NewLine,
                lblDetailMotivationGroupCode.Text,
                cmbDetailMotivationGroupCode.GetSelectedString(),
                lblDetailMotivationDetailCode.Text,
                txtDetailMotivationDetailCode.Text);
            return true;
        }

        // fired when tying in txtDetailMotivationDetailDesc
        private void DescriptionTyped(object sender, EventArgs e)
        {
            // syncs the two description text boxes if they should be synced
            if ((FDescription == txtDetailMotivationDetailDescLocal.Text) || string.IsNullOrEmpty(txtDetailMotivationDetailDescLocal.Text))
            {
                txtDetailMotivationDetailDescLocal.Text = txtDetailMotivationDetailDesc.Text;
            }

            FDescription = txtDetailMotivationDetailDesc.Text;
        }

        private void ValidateDataDetailsManual(AMotivationDetailRow ARow)
        {
            TVerificationResultCollection VerificationResultCollection = FPetraUtilsObject.VerificationResultCollection;
            DataColumn ValidationColumn;
            TValidationControlsData ValidationControlsData;
            TVerificationResult VerificationResult;
            Boolean KeyMinistryActive;

            // Partner Key must be for a Key Ministry and Key Ministry must not be deactivated
            if (Convert.ToInt64(txtDetailRecipientKey.Text) != 0)
            {
                ValidationColumn = FMainDS.AMotivationDetail[0].Table.Columns[AMotivationDetailTable.ColumnRecipientKeyId];

                if (FPetraUtilsObject.ValidationControlsDict.TryGetValue(ValidationColumn, out ValidationControlsData))
                {
                    if (TRemote.MFinance.Gift.WebConnectors.KeyMinistryExists(Convert.ToInt64(txtDetailRecipientKey.Text), out KeyMinistryActive))
                    {
                        if (!KeyMinistryActive)
                        {
                            // Key Ministry is deactivated and therefore can't be used here
                            VerificationResult = new TScreenVerificationResult(new TVerificationResult(this,
                                    ErrorCodes.GetErrorInfo(PetraErrorCodes.ERR_KEY_MINISTRY_DEACTIVATED)),
                                ValidationColumn, ValidationControlsData.ValidationControl);

                            // Handle addition to/removal from TVerificationResultCollection.
                            VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn);
                        }
                    }
                    else
                    {
                        // Partner Key does not refer to Key Ministry
                        VerificationResult = new TScreenVerificationResult(new TVerificationResult(this,
                                ErrorCodes.GetErrorInfo(PetraErrorCodes.ERR_NOT_A_KEY_MINISTRY)),
                            ValidationColumn, ValidationControlsData.ValidationControl);

                        // Handle addition to/removal from TVerificationResultCollection.
                        VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn);
                    }
                }
            }

            TSharedFinanceValidation_Gift.ValidateGiftMotivationSetupManual(this,
                ARow, FTaxDeductiblePercentageEnabled,
                ref VerificationResultCollection,
                FPetraUtilsObject.ValidationControlsDict);
        }

        #region Tax Deductibility Percentage

        private void SetupTaxDeductibilityControls()
        {
            // increase the width of the screen from the default width (760)
            if (ClientSize.Width == 760)
            {
                ClientSize = new System.Drawing.Size(920, ClientSize.Height);
            }

            // new label
            Label lblAccounts = new Label();
            lblAccounts.Name = "lblAccounts";
            lblAccounts.Location = lblDetailAccountCode.Location;
            lblAccounts.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            lblAccounts.Size = lblDetailAccountCode.Size;
            lblAccounts.Text = "Accounts:";
            lblAccounts.TextAlign = System.Drawing.ContentAlignment.TopRight;
            pnlDetails.Controls.Add(lblAccounts);

            // changes to DetailAccountCode control
            lblDetailAccountCode.Location = new System.Drawing.Point(lblDetailAccountCode.Location.X + 90, lblDetailAccountCode.Location.Y);
            lblDetailAccountCode.Text = "Non-Deductible:";
            lblDetailAccountCode.Size = new System.Drawing.Size(102, 17);
            cmbDetailAccountCode.Location = new System.Drawing.Point(cmbDetailAccountCode.Location.X + 105, cmbDetailAccountCode.Location.Y);

            // create new label and combobox for the Tax-Deductible Account Code
            Label lblDeductibleAccountCode = new Label();
            lblDeductibleAccountCode.Name = "lblDeductibleAccountCode";
            lblDeductibleAccountCode.Location = new System.Drawing.Point(cmbDetailAccountCode.Location.X + 310, lblDetailAccountCode.Location.Y);
            lblDeductibleAccountCode.Anchor =
                ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            lblDeductibleAccountCode.Size = new System.Drawing.Size(102, 17);
            lblDeductibleAccountCode.Text = "Tax-Deductible:";
            lblDeductibleAccountCode.TextAlign = System.Drawing.ContentAlignment.TopRight;
            pnlDetails.Controls.Add(lblDeductibleAccountCode);

            cmbDeductibleAccountCode = new TCmbAutoPopulated();
            cmbDeductibleAccountCode.Name = "cmbDeductibleAccountCode";
            cmbDeductibleAccountCode.Location = new System.Drawing.Point(cmbDetailAccountCode.Location.X + 415, cmbDetailAccountCode.Location.Y);
            cmbDeductibleAccountCode.Size = new System.Drawing.Size(300, 22);
            cmbDeductibleAccountCode.ListTable = TCmbAutoPopulated.TListTableEnum.UserDefinedList;
            cmbDeductibleAccountCode.TabIndex = cmbDetailAccountCode.TabIndex + 1;
            cmbDeductibleAccountCode.Validated += ControlValidatedHandler;
            pnlDetails.Controls.Add(cmbDeductibleAccountCode);

            TFinanceControls.InitialiseAccountList(ref cmbDeductibleAccountCode, FLedgerNumber, true, false, false, false);

            if (FMainDS.AMotivationDetail != null)
            {
                FPetraUtilsObject.ValidationControlsDict.Add(FMainDS.AMotivationDetail.Columns[(short)FMainDS.AMotivationDetail.GetType().GetField(
                                                                                                   "ColumnTaxDeductibleAccountId",
                                                                                                   BindingFlags.Public | BindingFlags.Static |
                                                                                                   BindingFlags.FlattenHierarchy).GetValue(FMainDS.
                                                                                                   AMotivationDetail.GetType())],
                    new TValidationControlsData(cmbDeductibleAccountCode, Catalog.GetString("Tax Deductible Account")));
            }

            // add new column to grid (TaxDeductibleAccount)
            grdDetails.Columns.Clear();
            grdDetails.AddTextColumn(Catalog.GetString("Group"), FMainDS.AMotivationDetail.ColumnMotivationGroupCode);
            grdDetails.AddTextColumn(Catalog.GetString("Motivation Detail"), FMainDS.AMotivationDetail.ColumnMotivationDetailCode);
            grdDetails.AddTextColumn(Catalog.GetString("Description"), FMainDS.AMotivationDetail.ColumnMotivationDetailDesc);
            grdDetails.AddTextColumn(Catalog.GetString("Non-Deductible Account"), FMainDS.AMotivationDetail.ColumnAccountCode);
            grdDetails.AddTextColumn(Catalog.GetString("Tax-Deductible Account"), FMainDS.AMotivationDetail.ColumnTaxDeductibleAccount);
            grdDetails.AddTextColumn(Catalog.GetString("Cost Centre Code"), FMainDS.AMotivationDetail.ColumnCostCentreCode);
            grdDetails.AddCheckBoxColumn(Catalog.GetString("Active"), FMainDS.AMotivationDetail.ColumnMotivationStatus);
            grdDetails.AddCheckBoxColumn(Catalog.GetString("Print Receipt"), FMainDS.AMotivationDetail.ColumnReceipt);

            SelectRowInGrid(1);
        }

        #endregion
    }
}