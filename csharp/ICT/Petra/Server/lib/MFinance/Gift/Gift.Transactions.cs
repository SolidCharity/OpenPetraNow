//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop, christophert
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

using Ict.Common;
using Ict.Common.Data;
using Ict.Common.DB;
using Ict.Common.Verification;
using Ict.Common.Verification.Exceptions;

using Ict.Petra.Server.App.Core;
using Ict.Petra.Server.App.Core.Security;
using Ict.Petra.Server.MFinance.Account.Data.Access;
using Ict.Petra.Server.MFinance.Gift.Data.Access;
using Ict.Petra.Server.MFinance.Setup.WebConnectors;
using Ict.Petra.Server.MFinance.Common;
using Ict.Petra.Server.MFinance.Cacheable;
using Ict.Petra.Server.MFinance.GL.WebConnectors;
using Ict.Petra.Server.MPartner.Partner.Data.Access;
using Ict.Petra.Server.MSysMan.Maintenance.SystemDefaults.WebConnectors;

using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.Gift.Data;
using Ict.Petra.Shared.MFinance.GL.Data;
using Ict.Petra.Shared.MPartner;
using Ict.Petra.Shared.MPartner.Partner.Data;

namespace Ict.Petra.Server.MFinance.Gift.WebConnectors
{
    ///<summary>
    /// This connector provides data for the finance Gift screens
    ///</summary>
    public partial class TGiftTransactionWebConnector
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ADateEffective"></param>
        /// <param name="ABatchDescription"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS CreateAGiftBatch(Int32 ALedgerNumber, DateTime ADateEffective, string ABatchDescription)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction ReadWriteTransaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                ALedgerTable LedgerTable = ALedgerAccess.LoadByPrimaryKey(ALedgerNumber, ReadWriteTransaction);

                TGiftBatchFunctions.CreateANewGiftBatchRow(ref MainDS, ref ReadWriteTransaction, ref LedgerTable, ALedgerNumber, ADateEffective);

                if (ABatchDescription.Length > 0)
                {
                    MainDS.AGiftBatch[0].BatchDescription = ABatchDescription;
                }

                AGiftBatchAccess.SubmitChanges(MainDS.AGiftBatch, ReadWriteTransaction);

                ALedgerAccess.SubmitChanges(LedgerTable, ReadWriteTransaction);

                MainDS.AGiftBatch.AcceptChanges();

                DBAccess.GDBAccessObj.CommitTransaction();
            }
            catch (Exception Exc)
            {
                TLogging.Log("An Exception occured during the creation of a Gift Batch record:" + Environment.NewLine + Exc.ToString());

                DBAccess.GDBAccessObj.RollbackTransaction();

                throw;
            }

            return MainDS;
        }

        /// <summary>
        /// create a new batch with a consecutive batch number in the ledger,
        /// and immediately store the batch and the new number in the database
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS CreateAGiftBatch(Int32 ALedgerNumber)
        {
            return CreateAGiftBatch(ALedgerNumber, DateTime.Today, Catalog.GetString("PLEASE ENTER DESCRIPTION"));
        }

        /// <summary>
        /// create a new recurring batch with a consecutive batch number in the ledger,
        /// and immediately store the batch and the new number in the database
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS CreateARecurringGiftBatch(Int32 ALedgerNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction ReadWriteTransaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                ALedgerTable LedgerTable = ALedgerAccess.LoadByPrimaryKey(ALedgerNumber, ReadWriteTransaction);

                TGiftBatchFunctions.CreateANewRecurringGiftBatchRow(ref MainDS, ref ReadWriteTransaction, ref LedgerTable, ALedgerNumber);

                ARecurringGiftBatchAccess.SubmitChanges(MainDS.ARecurringGiftBatch, ReadWriteTransaction);

                ALedgerAccess.SubmitChanges(LedgerTable, ReadWriteTransaction);

                MainDS.ARecurringGiftBatch.AcceptChanges();

                DBAccess.GDBAccessObj.CommitTransaction();
            }
            catch (Exception Exc)
            {
                TLogging.Log("An Exception occured during the creation of a Recurring Gift Batch record:" + Environment.NewLine + Exc.ToString());

                DBAccess.GDBAccessObj.RollbackTransaction();

                throw;
            }

            return MainDS;
        }

        /// <summary>
        /// create a gift batch from a recurring gift batch
        /// including gift and gift detail
        /// </summary>
        /// <param name="requestParams">HashTable with many parameters</param>
        [RequireModulePermission("FINANCE-1")]
        public static void SubmitRecurringGiftBatch(Hashtable requestParams)
        {
            GiftBatchTDS GMainDS = new GiftBatchTDS();
            Int32 ALedgerNumber = (Int32)requestParams["ALedgerNumber"];
            Int32 ABatchNumber = (Int32)requestParams["ABatchNumber"];
            DateTime AEffectiveDate = (DateTime)requestParams["AEffectiveDate"];
            Decimal AExchangeRateToBase = (Decimal)requestParams["AExchangeRateToBase"];
            Decimal AExchangeRateIntlToBase = (Decimal)requestParams["AExchangeRateIntlToBase"];

            bool TaxDeductiblePercentageEnabled = Convert.ToBoolean(
                TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_TAXDEDUCTIBLEPERCENTAGE, "FALSE"));

            bool TransactionInIntlCurrency = false;

            int NewGiftBatchNumber = -1;

            GiftBatchTDS RMainDS = LoadRecurringGiftTransactionsForBatch(ALedgerNumber, ABatchNumber);

            TDBTransaction Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                ALedgerTable LedgerTable = ALedgerAccess.LoadByPrimaryKey(ALedgerNumber, Transaction);
                ARecurringGiftBatchAccess.LoadByPrimaryKey(RMainDS, ALedgerNumber, ABatchNumber, Transaction);

                // Assuming all relevant data is loaded in RMainDS
                foreach (ARecurringGiftBatchRow recBatch  in RMainDS.ARecurringGiftBatch.Rows)
                {
                    if ((recBatch.BatchNumber == ABatchNumber) && (recBatch.LedgerNumber == ALedgerNumber))
                    {
                        Decimal batchTotal = 0;
                        AGiftBatchRow batch = TGiftBatchFunctions.CreateANewGiftBatchRow(ref GMainDS,
                            ref Transaction,
                            ref LedgerTable,
                            ALedgerNumber,
                            AEffectiveDate);

                        NewGiftBatchNumber = batch.BatchNumber;

                        batch.BatchDescription = recBatch.BatchDescription;
                        batch.BankCostCentre = recBatch.BankCostCentre;
                        batch.BankAccountCode = recBatch.BankAccountCode;
                        batch.ExchangeRateToBase = AExchangeRateToBase;
                        batch.MethodOfPaymentCode = recBatch.MethodOfPaymentCode;
                        batch.GiftType = recBatch.GiftType;
                        batch.HashTotal = recBatch.HashTotal;
                        batch.CurrencyCode = recBatch.CurrencyCode;

                        TransactionInIntlCurrency = (batch.CurrencyCode == LedgerTable[0].IntlCurrency);

                        foreach (ARecurringGiftRow recGift in RMainDS.ARecurringGift.Rows)
                        {
                            if ((recGift.BatchNumber == ABatchNumber) && (recGift.LedgerNumber == ALedgerNumber) && recGift.Active)
                            {
                                //Look if there is a detail which is in the donation period (else continue)
                                bool foundDetail = false;

                                foreach (ARecurringGiftDetailRow recGiftDetail in RMainDS.ARecurringGiftDetail.Rows)
                                {
                                    if ((recGiftDetail.GiftTransactionNumber == recGift.GiftTransactionNumber)
                                        && (recGiftDetail.BatchNumber == ABatchNumber) && (recGiftDetail.LedgerNumber == ALedgerNumber)
                                        && ((recGiftDetail.StartDonations == null) || (AEffectiveDate >= recGiftDetail.StartDonations))
                                        && ((recGiftDetail.EndDonations == null) || (AEffectiveDate <= recGiftDetail.EndDonations))
                                        )
                                    {
                                        foundDetail = true;
                                        break;
                                    }
                                }

                                if (!foundDetail)
                                {
                                    continue;
                                }

                                // make the gift from recGift
                                AGiftRow gift = GMainDS.AGift.NewRowTyped();
                                gift.LedgerNumber = batch.LedgerNumber;
                                gift.BatchNumber = batch.BatchNumber;
                                gift.GiftTransactionNumber = ++batch.LastGiftNumber;
                                gift.DonorKey = recGift.DonorKey;
                                gift.MethodOfGivingCode = recGift.MethodOfGivingCode;
                                gift.DateEntered = AEffectiveDate;

                                if (gift.MethodOfGivingCode.Length == 0)
                                {
                                    gift.SetMethodOfGivingCodeNull();
                                }

                                gift.MethodOfPaymentCode = recGift.MethodOfPaymentCode;

                                if (gift.MethodOfPaymentCode.Length == 0)
                                {
                                    gift.SetMethodOfPaymentCodeNull();
                                }

                                gift.Reference = recGift.Reference;
                                gift.ReceiptLetterCode = recGift.ReceiptLetterCode;


                                GMainDS.AGift.Rows.Add(gift);
                                //TODO (not here, but in the client or while posting) Check for Ex-OM Partner
                                //TODO (not here, but in the client or while posting) Check for expired key ministry (while Posting)

                                foreach (ARecurringGiftDetailRow recGiftDetail in RMainDS.ARecurringGiftDetail.Rows)
                                {
                                    //decimal amtIntl = 0M;
                                    decimal amtBase = 0M;
                                    decimal amtTrans = 0M;

                                    if ((recGiftDetail.GiftTransactionNumber == recGift.GiftTransactionNumber)
                                        && (recGiftDetail.BatchNumber == ABatchNumber) && (recGiftDetail.LedgerNumber == ALedgerNumber)
                                        && ((recGiftDetail.StartDonations == null) || (recGiftDetail.StartDonations <= AEffectiveDate))
                                        && ((recGiftDetail.EndDonations == null) || (recGiftDetail.EndDonations >= AEffectiveDate))
                                        )
                                    {
                                        AGiftDetailRow detail = GMainDS.AGiftDetail.NewRowTyped();
                                        detail.LedgerNumber = gift.LedgerNumber;
                                        detail.BatchNumber = gift.BatchNumber;
                                        detail.GiftTransactionNumber = gift.GiftTransactionNumber;
                                        detail.DetailNumber = ++gift.LastDetailNumber;

                                        amtTrans = recGiftDetail.GiftAmount;
                                        detail.GiftTransactionAmount = amtTrans;
                                        batchTotal += amtTrans;
                                        amtBase = GLRoutines.Divide((decimal)amtTrans, AExchangeRateToBase);
                                        detail.GiftAmount = amtBase;

                                        if (!TransactionInIntlCurrency)
                                        {
                                            detail.GiftAmountIntl = GLRoutines.Divide((decimal)amtBase, AExchangeRateIntlToBase);
                                        }
                                        else
                                        {
                                            detail.GiftAmountIntl = amtTrans;
                                        }

                                        detail.RecipientKey = recGiftDetail.RecipientKey;
                                        detail.RecipientLedgerNumber = recGiftDetail.RecipientLedgerNumber;

                                        detail.ChargeFlag = recGiftDetail.ChargeFlag;
                                        detail.ConfidentialGiftFlag = recGiftDetail.ConfidentialGiftFlag;
                                        detail.TaxDeductible = recGiftDetail.TaxDeductible;
                                        detail.MailingCode = recGiftDetail.MailingCode;

                                        if (detail.MailingCode.Length == 0)
                                        {
                                            detail.SetMailingCodeNull();
                                        }

                                        detail.MotivationGroupCode = recGiftDetail.MotivationGroupCode;
                                        detail.MotivationDetailCode = recGiftDetail.MotivationDetailCode;

                                        detail.GiftCommentOne = recGiftDetail.GiftCommentOne;
                                        detail.CommentOneType = recGiftDetail.CommentOneType;
                                        detail.GiftCommentTwo = recGiftDetail.GiftCommentTwo;
                                        detail.CommentTwoType = recGiftDetail.CommentTwoType;
                                        detail.GiftCommentThree = recGiftDetail.GiftCommentThree;
                                        detail.CommentThreeType = recGiftDetail.CommentThreeType;

                                        if (TaxDeductiblePercentageEnabled)
                                        {
                                            // Sets TaxDeductiblePct and uses it to calculate the tax deductibility amounts for a Gift Detail
                                            TGift.SetDefaultTaxDeductibilityData(ref detail, gift.DateEntered, Transaction);
                                        }

                                        GMainDS.AGiftDetail.Rows.Add(detail);
                                    }
                                }

                                batch.BatchTotal = batchTotal;
                            }
                        }
                    }
                }

                AGiftBatchAccess.SubmitChanges(GMainDS.AGiftBatch, Transaction);

                ALedgerAccess.SubmitChanges(LedgerTable, Transaction);

                AGiftAccess.SubmitChanges(GMainDS.AGift, Transaction);

                AGiftDetailAccess.SubmitChanges(GMainDS.AGiftDetail, Transaction);

                DBAccess.GDBAccessObj.CommitTransaction();

                GMainDS.AcceptChanges();
            }
            catch (Exception Exc)
            {
                TLogging.Log("An Exception occured during the submission of a Recurring Gift Batch:" + Environment.NewLine + Exc.ToString());

                DBAccess.GDBAccessObj.RollbackTransaction();

                GMainDS.RejectChanges();

                throw;
            }
        }

        /// <summary>
        /// Loads all available years with gift data into a table
        /// To be used by a combobox to select the financial year
        ///
        /// </summary>
        /// <returns>DataTable</returns>
        [RequireModulePermission("FINANCE-1")]
        public static DataTable GetAvailableGiftYears(Int32 ALedgerNumber, out String ADisplayMember, out String AValueMember)
        {
            ADisplayMember = "YearDate";
            AValueMember = "YearNumber";

            DataTable ReturnTable = new DataTable();
            ReturnTable.Columns.Add(AValueMember, typeof(System.Int32));
            ReturnTable.Columns.Add(ADisplayMember, typeof(String));
            ReturnTable.PrimaryKey = new DataColumn[] {
                ReturnTable.Columns[0]
            };

            System.Type TypeofTable = null;
            TCacheable CachePopulator = new TCacheable();

            ALedgerTable LedgerTable = (ALedgerTable)CachePopulator.GetCacheableTable(TCacheableFinanceTablesEnum.LedgerDetails,
                "",
                false,
                ALedgerNumber,
                out TypeofTable);

            AAccountingPeriodTable AccountingPeriods = (AAccountingPeriodTable)CachePopulator.GetCacheableTable(
                TCacheableFinanceTablesEnum.AccountingPeriodList,
                "",
                false,
                ALedgerNumber,
                out TypeofTable);

            AAccountingPeriodRow CurrentYearEndPeriod =
                (AAccountingPeriodRow)AccountingPeriods.Rows.Find(new object[] { ALedgerNumber, LedgerTable[0].NumberOfAccountingPeriods });
            DateTime CurrentYearEnd = CurrentYearEndPeriod.PeriodEndDate;

            TDBTransaction ReadTransaction = null;
            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref ReadTransaction,
                delegate
                {
                    // add the years, which are retrieved by reading from the gift batch tables
                    string Sql =
                        String.Format("SELECT DISTINCT {0} AS availYear FROM PUB_{1} WHERE {2}={3} ORDER BY 1 DESC",
                            AGiftBatchTable.GetBatchYearDBName(),
                            AGiftBatchTable.GetTableDBName(),
                            AGiftBatchTable.GetLedgerNumberDBName(),
                            ALedgerNumber);

                    DataTable BatchYearTable = DBAccess.GDBAccessObj.SelectDT(Sql, "BatchYearTable", ReadTransaction);

                    foreach (DataRow row in BatchYearTable.Rows)
                    {
                        DataRow resultRow = ReturnTable.NewRow();
                        resultRow[0] = row[0];
                        resultRow[1] = CurrentYearEnd.AddYears(-1 * (LedgerTable[0].CurrentFinancialYear - Convert.ToInt32(
                                                                         row[0]))).ToShortDateString();
                        ReturnTable.Rows.Add(resultRow);
                    }
                });

            // we should also check if the current year has been added, in case there are no gift batches yet
            if (ReturnTable.Rows.Find(LedgerTable[0].CurrentFinancialYear) == null)
            {
                DataRow resultRow = ReturnTable.NewRow();
                resultRow[0] = LedgerTable[0].CurrentFinancialYear;
                resultRow[1] = CurrentYearEnd.ToShortDateString();
                ReturnTable.Rows.InsertAt(resultRow, 0);
            }

            return ReturnTable;
        }

        /// <summary>
        /// returns ledger table for specified ledger
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadALedgerTable(Int32 ALedgerNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);
                });

            // Remove all Tables that were not filled with data before remoting them.
            MainDS.RemoveEmptyTables();

            // Accept row changes here so that the Client gets 'unmodified' rows
            MainDS.AcceptChanges();

            return MainDS;
        }

        /// <summary>
        /// loads a list of batches for the given ledger
        /// also get the ledger for the base currency etc
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="AYear">if -1, the year will be ignored</param>
        /// <param name="APeriod">if AYear is -1 or period is -1, the period will be ignored.
        /// if APeriod is 0 and the current year is selected, then the current and the forwarding periods are used.
        /// Period = -2 means all periods in current year</param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadAGiftBatchForYearPeriod(Int32 ALedgerNumber, Int32 AYear, Int32 APeriod)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();
            string FilterByPeriod = string.Empty;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    //Load Ledger table
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);

                    if (AYear > -1)
                    {
                        FilterByPeriod = String.Format(" AND PUB_{0}.{1} = {2}",
                            AGiftBatchTable.GetTableDBName(),
                            AGiftBatchTable.GetBatchYearDBName(),
                            AYear);

                        if ((APeriod == 0) && (AYear == MainDS.ALedger[0].CurrentFinancialYear))
                        {
                            //Return current and forwarding periods
                            FilterByPeriod += String.Format(" AND PUB_{0}.{1} >= {2}",
                                AGiftBatchTable.GetTableDBName(),
                                AGiftBatchTable.GetBatchPeriodDBName(),
                                MainDS.ALedger[0].CurrentPeriod);
                        }
                        else if (APeriod > 0)
                        {
                            //Return only specified period
                            FilterByPeriod += String.Format(" AND PUB_{0}.{1} = {2}",
                                AGiftBatchTable.GetTableDBName(),
                                AGiftBatchTable.GetBatchPeriodDBName(),
                                APeriod);
                        }
                        else
                        {
                            //Nothing to add, returns all periods
                        }
                    }

                    string SelectClause =
                        String.Format("SELECT * FROM PUB_{0} WHERE {1} = {2}",
                            AGiftBatchTable.GetTableDBName(),
                            AGiftBatchTable.GetLedgerNumberDBName(),
                            ALedgerNumber);

                    DBAccess.GDBAccessObj.Select(MainDS, SelectClause + FilterByPeriod,
                        MainDS.AGiftBatch.TableName, Transaction);
                });

            MainDS.AcceptChanges();

            return MainDS;
        }

        /// <summary>
        /// loads a list of batches for the given ledger's current year
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadAGiftBatchesForCurrentYear(Int32 ALedgerNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);

                    string SelectClause = String.Format("SELECT * FROM PUB_{0} WHERE {1} = {2} AND PUB_{0}.{3} = {4}",
                        AGiftBatchTable.GetTableDBName(),
                        AGiftBatchTable.GetLedgerNumberDBName(),
                        ALedgerNumber,
                        AGiftBatchTable.GetBatchYearDBName(),
                        MainDS.ALedger[0].CurrentFinancialYear);

                    DBAccess.GDBAccessObj.Select(MainDS, SelectClause, MainDS.AGiftBatch.TableName, Transaction);
                });

            MainDS.AcceptChanges();

            return MainDS;
        }

        /// <summary>
        /// loads a list of batches for the given ledger's current year and current plus forwarding periods
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadAGiftBatchesForCurrentYearPeriod(Int32 ALedgerNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);

                    string SelectClause = String.Format("SELECT * FROM PUB_{0} WHERE {1} = {2} AND PUB_{0}.{3} = {4} AND PUB_{0}.{5} >= {6}",
                        AGiftBatchTable.GetTableDBName(),
                        AGiftBatchTable.GetLedgerNumberDBName(),
                        ALedgerNumber,
                        AGiftBatchTable.GetBatchYearDBName(),
                        MainDS.ALedger[0].CurrentFinancialYear,
                        AGiftBatchTable.GetBatchPeriodDBName(),
                        MainDS.ALedger[0].CurrentPeriod);

                    DBAccess.GDBAccessObj.Select(MainDS, SelectClause, MainDS.AGiftBatch.TableName, Transaction);
                });

            return MainDS;
        }

        /// <summary>
        /// loads a GiftBatchTDS for a whole transaction (i.e. all details in the transaction)
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AGiftTransactionNumber"></param>
        /// <returns>DataSet containing the transation's data</returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadAGiftSingle(Int32 ALedgerNumber, Int32 ABatchNumber, Int32 AGiftTransactionNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);
                    AGiftDetailAccess.LoadViaAGift(MainDS, ALedgerNumber, ABatchNumber, AGiftTransactionNumber, Transaction);
                    AGiftAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, AGiftTransactionNumber, Transaction);
                    AGiftBatchAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, Transaction);
                });

            return MainDS;
        }

        /// <summary>
        /// loads a donor's last gift (if it exists) and returns the associated gift details
        /// </summary>
        /// <param name="ADonorPartnerKey"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDSAGiftDetailTable LoadDonorLastGift(Int64 ADonorPartnerKey)
        {
            TDBTransaction Transaction = null;
            GiftBatchTDSAGiftDetailTable ReturnValue = null;
            GiftBatchTDS MainDS = new GiftBatchTDS();

            DBAccess.GDBAccessObj.BeginAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    // load all gifts from donor
                    AGiftTable GiftTable = AGiftAccess.LoadViaPPartner(ADonorPartnerKey, Transaction);

                    if ((GiftTable == null) || (GiftTable.Rows.Count == 0))
                    {
                        return;
                    }

                    // find the most recent gift (probably the last gift in the table)
                    AGiftRow LatestGiftRow = (AGiftRow)GiftTable.Rows[GiftTable.Rows.Count - 1];

                    for (int i = GiftTable.Rows.Count - 2; i >= 0; i--)
                    {
                        if (LatestGiftRow.DateEntered < ((AGiftRow)GiftTable.Rows[i]).DateEntered)
                        {
                            LatestGiftRow = (AGiftRow)GiftTable.Rows[i];
                        }
                    }

                    // load gift details for the latest gift
                    AGiftDetailAccess.LoadViaAGift(MainDS, LatestGiftRow.LedgerNumber, LatestGiftRow.BatchNumber, LatestGiftRow.GiftTransactionNumber,
                        Transaction);
                    ReturnValue = MainDS.AGiftDetail;

                    if (ReturnValue.Rows.Count > 1)
                    {
                        // get the name of each recipient
                        foreach (GiftBatchTDSAGiftDetailRow Row in ReturnValue.Rows)
                        {
                            PPartnerRow RecipientRow = (PPartnerRow)PPartnerAccess.LoadByPrimaryKey(Row.RecipientKey, Transaction).Rows[0];
                            Row.RecipientDescription = RecipientRow.PartnerShortName;
                        }
                    }
                });

            return ReturnValue;
        }

        /// <summary>
        /// loads a list of recurring batches for the given ledger
        /// also get the ledger for the base currency etc
        /// TODO: limit to period, limit to batch status, etc
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadAGiftBatchSingle(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    MainDS = LoadAGiftBatchSingle(ALedgerNumber, ABatchNumber, ref Transaction);
                });

            return MainDS;
        }

        /// <summary>
        /// loads a list of recurring batches for the given ledger
        /// also get the ledger for the base currency etc
        /// TODO: limit to period, limit to batch status, etc
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="ATransaction"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        private static GiftBatchTDS LoadAGiftBatchSingle(Int32 ALedgerNumber, Int32 ABatchNumber, ref TDBTransaction ATransaction)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ATransaction);
            AGiftBatchAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, ATransaction);

            return MainDS;
        }

        /// <summary>
        /// loads a list of recurring batches for the given ledger
        /// also get the ledger for the base currency etc
        /// TODO: limit to period, limit to batch status, etc
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadARecurringGiftBatch(Int32 ALedgerNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);
                    ARecurringGiftBatchAccess.LoadViaALedger(MainDS, ALedgerNumber, Transaction);
                });

            return MainDS;
        }

        /// <summary>
        /// loads a list of recurring batches for the given ledger
        /// also get the ledger for the base currency etc
        /// TODO: limit to period, limit to batch status, etc
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadARecurringGiftBatchSingle(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);
                    ARecurringGiftBatchAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, Transaction);
                });

            return MainDS;
        }

        /// <summary>
        /// loads a list of gift transactions and details for the given ledger and batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadGiftAndTaxDeductDataForBatch(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = LoadAGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber, true);

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    try
                    {
                        // find PPartnerRows for all donors (needed for receipt frequency info)
                        foreach (AGiftRow Row in MainDS.AGift.Rows)
                        {
                            MainDS.DonorPartners.Merge(PPartnerAccess.LoadByPrimaryKey(Row.DonorKey, Transaction));
                        }
                    }
                    catch (Exception e)
                    {
                        TLogging.Log("Error in LoadTransactions: " + e.Message);
                    }
                });

            return MainDS;
        }

        /// <summary>
        /// When a reversed gift detail is cancelled this method will remove the Modified Detail flag on the original gift detail
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="AModifiedDetailKeys"></param>
        [RequireModulePermission("FINANCE-1")]
        public static void RemoveModifiedDetailOnCancel(Int32 ALedgerNumber, List <string>AModifiedDetailKeys)
        {
            TDBTransaction Transaction = null;
            bool SubmissionOK = true;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoTransaction(IsolationLevel.Serializable,
                ref Transaction,
                ref SubmissionOK,
                delegate
                {
                    try
                    {
                        // find PPartnerRows for all donors (needed for receipt frequency info)
                        foreach (string ModifiedDetailKey in AModifiedDetailKeys)
                        {
                            string[] GiftDetailFields = ModifiedDetailKey.Split('|');

                            AGiftDetailTable GiftDetailTable = AGiftDetailAccess.LoadByPrimaryKey(
                                ALedgerNumber, Convert.ToInt32(GiftDetailFields[1]),
                                Convert.ToInt32(GiftDetailFields[2]), Convert.ToInt32(GiftDetailFields[3]), Transaction);

                            GiftDetailTable[0].ModifiedDetail = false;

                            AGiftDetailAccess.SubmitChanges(GiftDetailTable, Transaction);
                        }
                    }
                    catch (Exception e)
                    {
                        TLogging.Log("Error in LoadTransactions: " + e.Message);
                    }
                });
        }

        /// <summary>
        /// Retrieve the cost centre code for the recipient
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ARecipientPartnerKey"></param>
        /// <param name="ARecipientLedgerNumber"></param>
        /// <param name="ADateGiftEntered"></param>
        /// <param name="AMotivationGroupCode"></param>
        /// <param name="AMotivationDetailCode"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static string RetrieveCostCentreCodeForRecipient(Int32 ALedgerNumber,
            Int64 ARecipientPartnerKey,
            Int64 ARecipientLedgerNumber,
            DateTime ADateGiftEntered,
            String AMotivationGroupCode,
            String AMotivationDetailCode)
        {
            string CostCentreCode = string.Empty;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    CostCentreCode = RetrieveCostCentreCodeForRecipient(ALedgerNumber,
                        ARecipientPartnerKey,
                        ARecipientLedgerNumber,
                        ADateGiftEntered,
                        AMotivationGroupCode,
                        AMotivationDetailCode,
                        Transaction);
                });

            return CostCentreCode;
        }

        /// <summary>
        /// Retrieve the cost centre code for the recipient
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ARecipientPartnerKey"></param>
        /// <param name="ARecipientLedgerNumber"></param>
        /// <param name="ADateGiftEntered"></param>
        /// <param name="AMotivationGroupCode"></param>
        /// <param name="AMotivationDetailCode"></param>
        /// <param name="ATransaction"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        private static string RetrieveCostCentreCodeForRecipient(Int32 ALedgerNumber,
            Int64 ARecipientPartnerKey,
            Int64 ARecipientLedgerNumber,
            DateTime ADateGiftEntered,
            String AMotivationGroupCode,
            String AMotivationDetailCode,
            TDBTransaction ATransaction)
        {
            string CostCentreCode = string.Empty;

            ALedgerTable LedgerTable = ALedgerAccess.LoadByPrimaryKey(ALedgerNumber, ATransaction);
            Int64 LedgerPartnerKey = LedgerTable[0].PartnerKey;

            //bool KeyMinIsActive = false;
            //bool KeyMinExists = KeyMinistryExists(ARecipientPartnerKey, out KeyMinIsActive);

            if (ARecipientPartnerKey > 0)
            {
                DataTable PartnerCostCentreTbl = Ict.Petra.Server.MFinance.Setup.WebConnectors.TGLSetupWebConnector.LoadCostCentrePartnerLinks(
                    ALedgerNumber,
                    ARecipientPartnerKey);

                if ((PartnerCostCentreTbl != null) && (PartnerCostCentreTbl.Rows.Count > 0))
                {
                    CostCentreCode = (string)PartnerCostCentreTbl.DefaultView[0].Row["IsLinked"];
                }
                else if (ARecipientLedgerNumber > 0)
                {
                    //Valid ledger number table
                    CheckCostCentreDestinationForRecipient(ALedgerNumber, ARecipientLedgerNumber,
                        out CostCentreCode);
                }
            }

            if (CostCentreCode.Length == 0)
            {
                AMotivationDetailTable MotivationDetailTable =
                    AMotivationDetailAccess.LoadByPrimaryKey(ALedgerNumber, AMotivationGroupCode, AMotivationDetailCode, ATransaction);

                if ((MotivationDetailTable != null) && (MotivationDetailTable.Rows.Count > 0))
                {
                    CostCentreCode = MotivationDetailTable[0].CostCentreCode;
                }
                else
                {
                    //Error condition which should never happen
                    throw new Exception(String.Format(Catalog.GetString(
                                "DATA QUALITY ERROR! Missing Cost Centre Code in Motivation Detail table for Partner {0} in Ledger {1}, Motivation Group {2} and Motivation Detail Code {3}!"),
                            ARecipientPartnerKey,
                            ALedgerNumber,
                            AMotivationGroupCode,
                            AMotivationDetailCode));
                }

                //if (ARecipientPartnerKey > 0
                //    && (ARecipientLedgerNumber != LedgerPartnerKey)
                //    && ((AMotivationGroupCode == MFinanceConstants.MOTIVATION_GROUP_GIFT)
                //        || KeyMinExists))
                //{
                //    //Error condition which should never happen
                //    throw new Exception(String.Format(Catalog.GetString("DATA QUALITY ERROR! Missing Cost Centre Code for Partner {0} in Ledger {1}!"),
                //            ARecipientPartnerKey,
                //            ALedgerNumber));
                //}
            }

            return CostCentreCode;
        }

        /// <summary>
        /// loads a list of recurring gift transactions and details for the given ledger and recurring batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadGiftTransactionsForBatch(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = LoadAGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber, true);

            // drop all tables apart from ARecurringGift and ARecurringGiftDetail
            foreach (DataTable table in MainDS.Tables)
            {
                if ((table.TableName != MainDS.AGift.TableName) && (table.TableName != MainDS.AGiftDetail.TableName))
                {
                    table.Clear();
                }
            }

            return MainDS;
        }

        /// <summary>
        /// loads a list of recurring gift transactions and details for the given ledger and recurring batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadRecurringGiftTransactionsForBatch(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = LoadARecurringGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber);

            // drop all tables apart from ARecurringGift and ARecurringGiftDetail
            foreach (DataTable table in MainDS.Tables)
            {
                if ((table.TableName != MainDS.ARecurringGift.TableName) && (table.TableName != MainDS.ARecurringGiftDetail.TableName))
                {
                    table.Clear();
                }
            }

            return MainDS;
        }

        /// <summary>
        /// loads a list of gift transactions and details for the given ledger and batch
        /// </summary>
        /// <param name="requestParams"></param>
        /// <param name="AMessages"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadDonorRecipientHistory(Hashtable requestParams,
            out TVerificationResultCollection AMessages)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            AMessages = new TVerificationResultCollection();

            string tempTableName = (string)requestParams["TempTable"];
            Int32 ledgerNumber = (Int32)requestParams["Ledger"];
            long recipientKey = (Int64)requestParams["Recipient"];
            long donorKey = (Int64)requestParams["Donor"];

            string dateFrom = (string)requestParams["DateFrom"];
            string dateTo = (string)requestParams["DateTo"];
            DateTime startDate;
            DateTime endDate;

            bool noDates = (dateFrom.Length == 0 && dateTo.Length == 0);

            string sqlStmt = string.Empty;

            try
            {
                Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.ReadCommitted);

                sqlStmt = TDataBase.ReadSqlFile("Gift.GetDonationsOfDonorAndOrRecipientTemplate.sql");

                OdbcParameter param;

                List <OdbcParameter>parameters = new List <OdbcParameter>();

                param = new OdbcParameter("LedgerNumber", OdbcType.Int);
                param.Value = ledgerNumber;
                parameters.Add(param);
                param = new OdbcParameter("DonorAny", OdbcType.Bit);
                param.Value = (donorKey == 0);
                parameters.Add(param);
                param = new OdbcParameter("DonorKey", OdbcType.BigInt);
                param.Value = donorKey;
                parameters.Add(param);
                param = new OdbcParameter("RecipientAny", OdbcType.Bit);
                param.Value = (recipientKey == 0);
                parameters.Add(param);
                param = new OdbcParameter("RecipientKey", OdbcType.BigInt);
                param.Value = recipientKey;
                parameters.Add(param);

                noDates = (dateFrom.Length == 0 && dateTo.Length == 0);
                param = new OdbcParameter("DateAny", OdbcType.Bit);
                param.Value = noDates;
                parameters.Add(param);

                if (noDates)
                {
                    //These values don't matter because of the value of noDate
                    startDate = new DateTime(2000, 1, 1);
                    endDate = new DateTime(2000, 1, 1);
                }
                else if ((dateFrom.Length > 0) && (dateTo.Length > 0))
                {
                    startDate = Convert.ToDateTime(dateFrom);     //, new CultureInfo("en-US"));
                    endDate = Convert.ToDateTime(dateTo);     //, new CultureInfo("en-US"));
                }
                else if (dateFrom.Length > 0)
                {
                    startDate = Convert.ToDateTime(dateFrom);
                    endDate = new DateTime(2050, 1, 1);
                }
                else
                {
                    startDate = new DateTime(1965, 1, 1);
                    endDate = Convert.ToDateTime(dateTo);
                }

                param = new OdbcParameter("DateFrom", OdbcType.Date);
                param.Value = startDate;
                parameters.Add(param);
                param = new OdbcParameter("DateTo", OdbcType.Date);
                param.Value = endDate;
                parameters.Add(param);

                //Load Ledger Table
                ALedgerAccess.LoadByPrimaryKey(MainDS, ledgerNumber, Transaction);

                //Can do this if needed: MainDS.DisableConstraints();
                DBAccess.GDBAccessObj.SelectToTempTable(MainDS, sqlStmt, tempTableName, Transaction, parameters.ToArray(), 0, 0);

                MainDS.Tables[tempTableName].Columns.Add("DonorDescription");

                PPartnerTable Tbl = null;

                // Two scenarios. 1. The donor key is not set which means the Donor Description could be different for every record.
                if (donorKey == 0)
                {
                    Tbl = PPartnerAccess.LoadAll(Transaction);

                    foreach (DataRow Row in MainDS.Tables[tempTableName].Rows)
                    {
                        Row["DonorDescription"] = ((PPartnerRow)Tbl.Rows.Find(new object[] { Convert.ToInt64(Row["DonorKey"]) })).PartnerShortName;
                    }
                }
                // 2. The donor key is set which means the Donor Description will be the same for every record. (Less calculations this way.)
                else
                {
                    Tbl = PPartnerAccess.LoadByPrimaryKey(donorKey, Transaction);

                    foreach (DataRow Row in MainDS.Tables[tempTableName].Rows)
                    {
                        Row["DonorDescription"] = Tbl[0].PartnerShortName;
                    }
                }

                MainDS.AcceptChanges();
            }
            finally
            {
                if (Transaction != null)
                {
                    DBAccess.GDBAccessObj.RollbackTransaction();
                }
            }
            return MainDS;
        }

        /// <summary>
        /// this will store all new and modified batches, gift transactions and details
        /// </summary>
        /// <param name="AInspectDS"></param>
        /// <param name="AVerificationResult"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static TSubmitChangesResult SaveGiftBatchTDS(ref GiftBatchTDS AInspectDS,
            out TVerificationResultCollection AVerificationResult)
        {
            TSubmitChangesResult SubmissionResult = TSubmitChangesResult.scrError;

            AVerificationResult = new TVerificationResultCollection();

            // make sure that empty tables are removed !! This can return NULL!
            AInspectDS = AInspectDS.GetChangesTyped(true);

            if (AInspectDS == null)
            {
                AVerificationResult.Add(new TVerificationResult(
                        Catalog.GetString("Save Gift Batch"),
                        Catalog.GetString("No changes - nothing to do"),
                        TResultSeverity.Resv_Info));
                return TSubmitChangesResult.scrNothingToBeSaved;
            }

            bool AllValidationsOK = true;

            bool GiftBatchTableInDataSet = (AInspectDS.AGiftBatch != null);
            bool GiftTableInDataSet = (AInspectDS.AGift != null);
            bool GiftDetailTableInDataSet = (AInspectDS.AGiftDetail != null);

            bool RecurrGiftBatchTableInDataSet = (AInspectDS.ARecurringGiftBatch != null);
            bool RecurrGiftTableInDataSet = (AInspectDS.ARecurringGift != null);
            bool RecurrGiftDetailTableInDataSet = (AInspectDS.ARecurringGiftDetail != null);

            if (RecurrGiftBatchTableInDataSet || RecurrGiftTableInDataSet || RecurrGiftDetailTableInDataSet)
            {
                if (GiftBatchTableInDataSet || GiftTableInDataSet || GiftDetailTableInDataSet)
                {
                    throw new Exception(
                        "SaveGiftBatchTDS: need to call GetChangesTyped before saving, otherwise confusion about recurring or normal gl batch");
                }

                return SaveRecurringGiftBatchTDS(ref AInspectDS, ref AVerificationResult);
            }

            //Get a list of all batches involved
            List <Int32>ListAllGiftBatchesToProcess = new List <int>();

            DataView AllBatchesToProcess = new DataView(AInspectDS.AGiftBatch);
            AllBatchesToProcess.RowStateFilter = DataViewRowState.OriginalRows;

            foreach (DataRowView drv in AllBatchesToProcess)
            {
                ListAllGiftBatchesToProcess.Add((int)(drv[AGiftBatchTable.ColumnBatchNumberId]));
            }

            if (GiftBatchTableInDataSet)
            {
                ValidateGiftBatch(ref AVerificationResult, AInspectDS.AGiftBatch);
                ValidateGiftBatchManual(ref AVerificationResult, AInspectDS.AGiftBatch);

                if (!TVerificationHelper.IsNullOrOnlyNonCritical(AVerificationResult))
                {
                    AllValidationsOK = false;
                }
            }

            if (GiftDetailTableInDataSet)
            {
                ValidateGiftDetail(ref AVerificationResult, AInspectDS.AGiftDetail);
                ValidateGiftDetailManual(ref AVerificationResult, AInspectDS.AGiftDetail);

                if (!TVerificationHelper.IsNullOrOnlyNonCritical(AVerificationResult))
                {
                    AllValidationsOK = false;
                }
            }

            if (AVerificationResult.Count > 0)
            {
                // Downgrade TScreenVerificationResults to TVerificationResults in order to allow
                // Serialisation (needed for .NET Remoting).
                TVerificationResultCollection.DowngradeScreenVerificationResults(AVerificationResult);
            }

            if (AllValidationsOK)
            {
                int giftBatchCount = 0;
                int giftCount = 0;
                int giftDetailCount = 0;

                if (GiftBatchTableInDataSet)
                {
                    giftBatchCount = AInspectDS.AGiftBatch.Count;
                }

                if (GiftTableInDataSet)
                {
                    giftCount = AInspectDS.AGift.Count;
                }

                if (GiftDetailTableInDataSet)
                {
                    giftDetailCount = AInspectDS.AGiftDetail.Count;
                }

                if ((giftCount > 0) && (giftDetailCount > 1))
                {
                    //The Gift Detail table must be in ascending order
                    AGiftDetailTable cloneDetail = (AGiftDetailTable)AInspectDS.AGiftDetail.Clone();

                    foreach (int batchNumber in ListAllGiftBatchesToProcess)
                    {
                        //Copy across any rows marked as deleted first.
                        DataView giftDetails1 = new DataView(AInspectDS.AGiftDetail);
                        giftDetails1.RowFilter = string.Format("{0}={1}",
                            AGiftDetailTable.GetBatchNumberDBName(),
                            batchNumber);
                        giftDetails1.RowStateFilter = DataViewRowState.Deleted;

                        foreach (DataRowView drv in giftDetails1)
                        {
                            AGiftDetailRow gDetailRow = (AGiftDetailRow)drv.Row;
                            cloneDetail.ImportRow(gDetailRow);
                        }

                        //Import the other rows in ascending order
                        DataView giftDetails2 = new DataView(AInspectDS.AGiftDetail);
                        giftDetails2.RowFilter = string.Format("{0}={1}",
                            AGiftDetailTable.GetBatchNumberDBName(),
                            batchNumber);

                        giftDetails2.Sort = String.Format("{0} ASC, {1} ASC, {2} ASC",
                            AGiftDetailTable.GetBatchNumberDBName(),
                            AGiftDetailTable.GetGiftTransactionNumberDBName(),
                            AGiftDetailTable.GetDetailNumberDBName());

                        foreach (DataRowView giftDetailRows in giftDetails2)
                        {
                            AGiftDetailRow gDR = (AGiftDetailRow)giftDetailRows.Row;
                            cloneDetail.ImportRow(gDR);
                        }
                    }

                    //Clear the table and import the rows from the clone
                    AInspectDS.AGiftDetail.Clear();

                    for (int i = 0; i < giftDetailCount; i++)
                    {
                        AGiftDetailRow gDR2 = (AGiftDetailRow)cloneDetail[i];
                        AInspectDS.AGiftDetail.ImportRow(gDR2);
                    }
                }

                GiftBatchTDSAccess.SubmitChanges(AInspectDS);

                SubmissionResult = TSubmitChangesResult.scrOK;

                if (GiftTableInDataSet)
                {
                    if (GiftDetailTableInDataSet)
                    {
                        AInspectDS.AGiftDetail.AcceptChanges();
                    }

                    AInspectDS.AGift.AcceptChanges();

                    if (AInspectDS.AGift.Count > 0)
                    {
                        AGiftRow tranR = (AGiftRow)AInspectDS.AGift.Rows[0];

                        Int32 currentLedger = tranR.LedgerNumber;
                        Int32 currentBatch = tranR.BatchNumber;
                        Int32 giftToDelete = 0;

                        try
                        {
                            DataRow[] foundGiftsForDeletion = AInspectDS.AGift.Select(String.Format("{0} = '{1}'",
                                    AGiftTable.GetGiftStatusDBName(),
                                    MFinanceConstants.MARKED_FOR_DELETION));

                            if (foundGiftsForDeletion.Length > 0)
                            {
                                AGiftRow giftRowClient = null;

                                for (int i = 0; i < foundGiftsForDeletion.Length; i++)
                                {
                                    //A gift has been deleted
                                    giftRowClient = (AGiftRow)foundGiftsForDeletion[i];

                                    giftToDelete = giftRowClient.GiftTransactionNumber;
                                    TLogging.Log(String.Format("Gift to Delete: {0} from Batch: {1}",
                                            giftToDelete,
                                            currentBatch));

                                    giftRowClient.Delete();
                                }
                            }

                            GiftBatchTDSAccess.SubmitChanges(AInspectDS);

                            SubmissionResult = TSubmitChangesResult.scrOK;
                        }
                        catch (Exception ex)
                        {
                            TLogging.Log("Saving DataSet: " + ex.Message);

                            TLogging.Log(String.Format("Error trying to save changes: {0} in Batch: {1}",
                                    giftToDelete,
                                    currentBatch
                                    ));

                            SubmissionResult = TSubmitChangesResult.scrError;
                        }
                    }
                }
            }

            return SubmissionResult;
        }

        private bool CheckGiftNumbersAreConsecutive()
        {
            //TODO
            return true;
        }

        /// <summary>
        /// Returns a table of gifts with Ex-Worker recipients
        /// </summary>
        /// <param name="AGiftDetailsToCheck">GiftDetails to check for ExWorker recipients</param>
        /// <param name="ANotInBatchNumber">Used to exclude gift from a particular batch</param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static DataTable FindGiftRecipientExWorker(DataTable AGiftDetailsToCheck, int ANotInBatchNumber = -1)
        {
            DataTable ReturnValue = AGiftDetailsToCheck.Copy();

            ReturnValue.Clear();

            TDBTransaction Transaction = null;
            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    foreach (DataRow Row in AGiftDetailsToCheck.Rows)
                    {
                        // check changed data is either added or modified and that it is by a new donor
                        if ((Row.RowState != DataRowState.Deleted)
                            && (((Int32)Row[GiftBatchTDSAGiftDetailTable.GetBatchNumberDBName()]) != ANotInBatchNumber))
                        {
                            PPartnerTypeTable PartnerTypeTable =
                                PPartnerTypeAccess.LoadViaPPartner((Int64)Row[GiftBatchTDSAGiftDetailTable.GetRecipientKeyDBName()], Transaction);

                            foreach (PPartnerTypeRow TypeRow in PartnerTypeTable.Rows)
                            {
                                if (TypeRow.TypeCode.StartsWith(TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_EXWORKERSPECIALTYPE,
                                            "EX-WORKER")))
                                {
                                    ReturnValue.Rows.Add((object[])Row.ItemArray.Clone());
                                    break;
                                }
                            }
                        }
                    }
                });

            return ReturnValue;
        }

        /// <summary>
        /// this will store all new and modified recurring batches, recurring gift transactions and recurring details
        /// </summary>
        /// <param name="AInspectDS"></param>
        /// <param name="AVerificationResult"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        private static TSubmitChangesResult SaveRecurringGiftBatchTDS(ref GiftBatchTDS AInspectDS,
            ref TVerificationResultCollection AVerificationResult)
        {
            TSubmitChangesResult SubmissionResult = TSubmitChangesResult.scrError;

            bool AllValidationsOK = true;

            bool RecurrGiftBatchTableInDataSet = (AInspectDS.ARecurringGiftBatch != null);
            bool RecurrGiftTableInDataSet = (AInspectDS.ARecurringGift != null);
            bool RecurrGiftDetailTableInDataSet = (AInspectDS.ARecurringGiftDetail != null);

            if (RecurrGiftBatchTableInDataSet)
            {
                ValidateRecurringGiftBatch(ref AVerificationResult, AInspectDS.ARecurringGiftBatch);
                ValidateRecurringGiftBatchManual(ref AVerificationResult, AInspectDS.ARecurringGiftBatch);

                if (!TVerificationHelper.IsNullOrOnlyNonCritical(AVerificationResult))
                {
                    AllValidationsOK = false;
                }
            }

            if (RecurrGiftDetailTableInDataSet)
            {
                ValidateRecurringGiftDetail(ref AVerificationResult, AInspectDS.ARecurringGiftDetail);
                ValidateRecurringGiftDetailManual(ref AVerificationResult, AInspectDS.ARecurringGiftDetail);

                if (!TVerificationHelper.IsNullOrOnlyNonCritical(AVerificationResult))
                {
                    AllValidationsOK = false;
                }
            }

            if (AVerificationResult.Count > 0)
            {
                // Downgrade TScreenVerificationResults to TVerificationResults in order to allow
                // Serialisation (needed for .NET Remoting).
                TVerificationResultCollection.DowngradeScreenVerificationResults(AVerificationResult);
            }

            //Get a list of all batches involved
            List <Int32>ListAllGiftBatchesToProcess = new List <int>();

            DataView AllBatchesToProcess = new DataView(AInspectDS.ARecurringGiftBatch);
            AllBatchesToProcess.RowStateFilter = DataViewRowState.OriginalRows;

            foreach (DataRowView drv in AllBatchesToProcess)
            {
                ListAllGiftBatchesToProcess.Add((int)(drv[ARecurringGiftBatchTable.ColumnBatchNumberId]));
            }

            //Get a list of all batches to delete - for multi-delete
            //List <Int32>ListAllGiftBatchesToDelete = new List <int>();

            //DataView AllBatchesToDelete = new DataView(AInspectDS.ARecurringGiftBatch);
            //AllBatchesToDelete.RowStateFilter = DataViewRowState.Deleted;

            //foreach (DataRowView drv in AllBatchesToDelete)
            //{
            //    ListAllGiftBatchesToDelete.Add((int)(drv[ARecurringGiftBatchTable.ColumnBatchNumberId]));
            //}

            if (AllValidationsOK)
            {
                int recurrGiftBatchCount = 0;
                int recurrGiftCount = 0;
                int recurrGiftDetailCount = 0;

                if (RecurrGiftBatchTableInDataSet)
                {
                    recurrGiftBatchCount = AInspectDS.ARecurringGiftBatch.Count;
                }

                if (RecurrGiftTableInDataSet)
                {
                    recurrGiftCount = AInspectDS.ARecurringGift.Count;
                }

                if (RecurrGiftDetailTableInDataSet)
                {
                    recurrGiftDetailCount = AInspectDS.ARecurringGiftDetail.Count;
                }

                if ((recurrGiftBatchCount > 0) && (recurrGiftCount > 0) && (recurrGiftDetailCount > 1))
                {
                    //The Gift Detail table must be in ascending order
                    ARecurringGiftDetailTable cloneDetail = (ARecurringGiftDetailTable)AInspectDS.ARecurringGiftDetail.Clone();

                    foreach (int batchNumber in ListAllGiftBatchesToProcess)
                    {
                        //Copy across any rows marked as deleted first.
                        DataView giftDetails1 = new DataView(AInspectDS.ARecurringGiftDetail);
                        giftDetails1.RowFilter = string.Format("{0}={1}",
                            ARecurringGiftDetailTable.GetBatchNumberDBName(),
                            batchNumber);
                        giftDetails1.RowStateFilter = DataViewRowState.Deleted;

                        foreach (DataRowView drv in giftDetails1)
                        {
                            ARecurringGiftDetailRow gDeletedDetailRow = (ARecurringGiftDetailRow)drv.Row;
                            cloneDetail.ImportRow(gDeletedDetailRow);
                        }

                        //Import the other rows in ascending order
                        DataView giftDetails2 = new DataView(AInspectDS.ARecurringGiftDetail);
                        giftDetails2.RowFilter = string.Format("{0}={1}",
                            ARecurringGiftDetailTable.GetBatchNumberDBName(),
                            batchNumber);

                        giftDetails2.Sort = String.Format("{0} ASC, {1} ASC, {2} ASC",
                            ARecurringGiftDetailTable.GetBatchNumberDBName(),
                            ARecurringGiftDetailTable.GetGiftTransactionNumberDBName(),
                            ARecurringGiftDetailTable.GetDetailNumberDBName());

                        foreach (DataRowView giftDetailRows in giftDetails2)
                        {
                            ARecurringGiftDetailRow gDetailRow = (ARecurringGiftDetailRow)giftDetailRows.Row;
                            cloneDetail.ImportRow(gDetailRow);
                        }
                    }

                    //Clear the table and import the rows from the clone
                    AInspectDS.ARecurringGiftDetail.Clear();

                    for (int i = 0; i < recurrGiftDetailCount; i++)
                    {
                        ARecurringGiftDetailRow gDR2 = (ARecurringGiftDetailRow)cloneDetail[i];
                        AInspectDS.ARecurringGiftDetail.ImportRow(gDR2);
                    }
                }

                GiftBatchTDSAccess.SubmitChanges(AInspectDS);

                SubmissionResult = TSubmitChangesResult.scrOK;

                if (RecurrGiftTableInDataSet)
                {
                    if (RecurrGiftDetailTableInDataSet)
                    {
                        AInspectDS.ARecurringGiftDetail.AcceptChanges();
                    }

                    AInspectDS.ARecurringGift.AcceptChanges();

                    if (AInspectDS.ARecurringGift.Count > 0)
                    {
                        ARecurringGiftRow tranR = (ARecurringGiftRow)AInspectDS.ARecurringGift.Rows[0];

                        Int32 currentLedger = tranR.LedgerNumber;
                        Int32 currentBatch = tranR.BatchNumber;
                        Int32 giftToDelete = 0;

                        try
                        {
                            DataRow[] foundGiftsForDeletion = AInspectDS.ARecurringGift.Select(String.Format("{0} = '{1}'",
                                    ARecurringGiftTable.GetChargeStatusDBName(),
                                    MFinanceConstants.MARKED_FOR_DELETION));

                            if (foundGiftsForDeletion.Length > 0)
                            {
                                ARecurringGiftRow giftRowClient = null;

                                for (int i = 0; i < foundGiftsForDeletion.Length; i++)
                                {
                                    //A gift has been deleted
                                    giftRowClient = (ARecurringGiftRow)foundGiftsForDeletion[i];

                                    giftToDelete = giftRowClient.GiftTransactionNumber;
                                    TLogging.Log(String.Format("Gift to Delete: {0} from Batch: {1}",
                                            giftToDelete,
                                            currentBatch));

                                    giftRowClient.Delete();
                                }
                            }

                            GiftBatchTDSAccess.SubmitChanges(AInspectDS);

                            SubmissionResult = TSubmitChangesResult.scrOK;
                        }
                        catch (Exception ex)
                        {
                            TLogging.Log("Saving DataSet: " + ex.Message);

                            TLogging.Log(String.Format("Error trying to save changes: {0} in recurring Batch: {1}",
                                    giftToDelete,
                                    currentBatch
                                    ));

                            SubmissionResult = TSubmitChangesResult.scrError;
                        }
                    }
                }
            }

            return SubmissionResult;
        }

        /// <summary>
        /// creates the GL batch needed for posting the gift batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="AGiftDataset"></param>
        /// <returns></returns>
        private static GLBatchTDS CreateGLBatchAndTransactionsForPostingGifts(Int32 ALedgerNumber, ref GiftBatchTDS AGiftDataset)
        {
            // create one GL batch
            GLBatchTDS GLDataset = TGLTransactionWebConnector.CreateABatch(ALedgerNumber);

            ABatchRow batch = GLDataset.ABatch[0];

            AGiftBatchRow giftBatch = AGiftDataset.AGiftBatch[0];

            bool TaxDeductiblePercentageEnabled = Convert.ToBoolean(
                TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_TAXDEDUCTIBLEPERCENTAGE, "FALSE"));

            batch.BatchDescription = Catalog.GetString("Gift Batch " + giftBatch.BatchNumber.ToString());
            batch.DateEffective = giftBatch.GlEffectiveDate;
            batch.BatchPeriod = giftBatch.BatchPeriod;
            batch.GiftBatchNumber = giftBatch.BatchNumber;
            batch.BatchStatus = MFinanceConstants.BATCH_UNPOSTED;

            // one gift batch only has one currency, create only one journal
            AJournalRow journal = GLDataset.AJournal.NewRowTyped();
            journal.LedgerNumber = batch.LedgerNumber;
            journal.BatchNumber = batch.BatchNumber;
            journal.JournalNumber = 1;
            journal.DateEffective = batch.DateEffective;
            journal.JournalPeriod = giftBatch.BatchPeriod;
            journal.TransactionCurrency = giftBatch.CurrencyCode;
            journal.ExchangeRateToBase = giftBatch.ExchangeRateToBase;
            journal.ExchangeRateTime = 7200; //represents 2 hours into the date, i.e. 2am
            journal.JournalDescription = batch.BatchDescription;
            journal.TransactionTypeCode = CommonAccountingTransactionTypesEnum.GR.ToString();
            journal.SubSystemCode = CommonAccountingSubSystemsEnum.GR.ToString();
            journal.LastTransactionNumber = 0;
            journal.DateOfEntry = DateTime.Now;

            GLDataset.AJournal.Rows.Add(journal);

            foreach (GiftBatchTDSAGiftDetailRow giftdetail in AGiftDataset.AGiftDetail.Rows)
            {
                if (!TaxDeductiblePercentageEnabled)
                {
                    AddGiftDetailToGLBatch(ref GLDataset, giftdetail.CostCentreCode, giftdetail.AccountCode,
                        giftdetail.GiftTransactionAmount, giftdetail.GiftAmount, giftdetail.GiftAmountIntl, journal, giftBatch);
                }
                else if (!giftdetail.IsTaxDeductiblePctNull())
                {
                    // if tax deductible pct is enabled then the gift detail needs split in two: tax-deductible and non-deductible
                    if (giftdetail.TaxDeductiblePct > 0)
                    {
                        // tax deductible
                        AddGiftDetailToGLBatch(ref GLDataset,
                            giftdetail.CostCentreCode,
                            giftdetail.TaxDeductibleAccountCode,
                            giftdetail.TaxDeductibleAmount,
                            giftdetail.TaxDeductibleAmountBase,
                            giftdetail.TaxDeductibleAmountIntl,
                            journal,
                            giftBatch);
                    }

                    if (giftdetail.TaxDeductiblePct < 100)
                    {
                        // non deductible
                        AddGiftDetailToGLBatch(ref GLDataset,
                            giftdetail.CostCentreCode,
                            giftdetail.AccountCode,
                            giftdetail.NonDeductibleAmount,
                            giftdetail.NonDeductibleAmountBase,
                            giftdetail.NonDeductibleAmountIntl,
                            journal,
                            giftBatch);
                    }
                }

                // TODO: for other currencies a post to a_ledger.a_forex_gains_losses_account_c ???

                // TODO: do the fee calculation, a_fees_payable, a_fees_receivable
            }

            ATransactionRow transactionForTotals = GLDataset.ATransaction.NewRowTyped();
            transactionForTotals.LedgerNumber = journal.LedgerNumber;
            transactionForTotals.BatchNumber = journal.BatchNumber;
            transactionForTotals.JournalNumber = journal.JournalNumber;
            transactionForTotals.TransactionNumber = ++journal.LastTransactionNumber;
            transactionForTotals.TransactionAmount = 0;
            transactionForTotals.AmountInBaseCurrency = 0;
            transactionForTotals.AmountInIntlCurrency = 0;
            transactionForTotals.TransactionDate = giftBatch.GlEffectiveDate;
            transactionForTotals.SystemGenerated = true;

            foreach (ATransactionRow transactionRow in GLDataset.ATransaction.Rows)
            {
                if (transactionRow.DebitCreditIndicator)
                {
                    transactionForTotals.TransactionAmount -= transactionRow.TransactionAmount;
                    transactionForTotals.AmountInBaseCurrency -= transactionRow.AmountInBaseCurrency;
                    transactionForTotals.AmountInIntlCurrency -= transactionRow.AmountInIntlCurrency;
                }
                else
                {
                    transactionForTotals.TransactionAmount += transactionRow.TransactionAmount;
                    transactionForTotals.AmountInBaseCurrency += transactionRow.AmountInBaseCurrency;
                    transactionForTotals.AmountInIntlCurrency += transactionRow.AmountInIntlCurrency;
                }
            }

            // determine whether transaction is debit or credit
            transactionForTotals.DebitCreditIndicator = transactionForTotals.TransactionAmount > 0;

            transactionForTotals.TransactionAmount = Math.Abs(transactionForTotals.TransactionAmount);
            transactionForTotals.AmountInBaseCurrency = Math.Abs(transactionForTotals.AmountInBaseCurrency);
            transactionForTotals.AmountInIntlCurrency = Math.Abs(transactionForTotals.AmountInIntlCurrency);

            // TODO: account and costcentre based on linked costcentre, current commitment, and Motivation detail
            // if motivation cost centre is a summary cost centre, make sure the transaction costcentre is reporting to that summary cost centre
            // Careful: modify gift cost centre and account and recipient field only when the amount is positive.
            // adjustments and reversals must remain on the original value
            transactionForTotals.AccountCode = giftBatch.BankAccountCode;
            transactionForTotals.CostCentreCode = TLedgerInfo.GetStandardCostCentre(ALedgerNumber);
            transactionForTotals.Narrative = "Deposit from receipts - Gift Batch " + giftBatch.BatchNumber.ToString();
            transactionForTotals.Reference = "GB" + giftBatch.BatchNumber.ToString();

            // it is possible that the total transaction amount is 0 in which case we do not need this transaction
            if (transactionForTotals.TransactionAmount != 0)
            {
                GLDataset.ATransaction.Rows.Add(transactionForTotals);
            }

            GLDataset.ATransaction.DefaultView.RowFilter = string.Empty;

            foreach (ATransactionRow transaction in GLDataset.ATransaction.Rows)
            {
                if (transaction.DebitCreditIndicator)
                {
                    journal.JournalDebitTotal += transaction.TransactionAmount;
                    batch.BatchDebitTotal += transaction.TransactionAmount;
                }
                else
                {
                    journal.JournalCreditTotal += transaction.TransactionAmount;
                    batch.BatchCreditTotal += transaction.TransactionAmount;
                }
            }

            batch.LastJournal = 1;

            return GLDataset;
        }

        private static void AddGiftDetailToGLBatch(ref GLBatchTDS AGLDataset,
            string ACostCentre, string AAccountCode, decimal ATransactionAmount, decimal AAmountInBaseCurrency, decimal AAmountInIntlCurrency,
            AJournalRow AJournal, AGiftBatchRow AGiftBatch)
        {
            ATransactionRow transaction = null;

            // do we have already a transaction for this costcentre&account?
            AGLDataset.ATransaction.DefaultView.RowFilter = String.Format("{0}='{1}' and {2}='{3}'",
                ATransactionTable.GetAccountCodeDBName(),
                AAccountCode,
                ATransactionTable.GetCostCentreCodeDBName(),
                ACostCentre);

            if (AGLDataset.ATransaction.DefaultView.Count == 0)
            {
                transaction = AGLDataset.ATransaction.NewRowTyped();
                transaction.LedgerNumber = AJournal.LedgerNumber;
                transaction.BatchNumber = AJournal.BatchNumber;
                transaction.JournalNumber = AJournal.JournalNumber;
                transaction.TransactionNumber = ++AJournal.LastTransactionNumber;
                transaction.AccountCode = AAccountCode;
                transaction.CostCentreCode = ACostCentre;
                transaction.Narrative = "GB - Gift Batch " + AGiftBatch.BatchNumber.ToString();
                transaction.Reference = "GB" + AGiftBatch.BatchNumber.ToString();
                transaction.DebitCreditIndicator = ATransactionAmount < 0;
                transaction.TransactionAmount = 0;
                transaction.AmountInBaseCurrency = 0;
                transaction.AmountInIntlCurrency = 0;
                transaction.SystemGenerated = true;
                transaction.TransactionDate = AGiftBatch.GlEffectiveDate;

                AGLDataset.ATransaction.Rows.Add(transaction);
            }
            else
            {
                transaction = (ATransactionRow)AGLDataset.ATransaction.DefaultView[0].Row;
            }

            // if gift has same debit/credit indicator as transaction
            if (transaction.DebitCreditIndicator == ATransactionAmount < 0)
            {
                transaction.TransactionAmount += Math.Abs(ATransactionAmount);
                transaction.AmountInBaseCurrency += Math.Abs(AAmountInBaseCurrency);
                transaction.AmountInIntlCurrency += Math.Abs(AAmountInIntlCurrency);
            }
            // if gift has a different debit/credit indicator as transaction
            else
            {
                transaction.TransactionAmount -= Math.Abs(ATransactionAmount);
                transaction.AmountInBaseCurrency -= Math.Abs(AAmountInBaseCurrency);
                transaction.AmountInIntlCurrency -= Math.Abs(AAmountInIntlCurrency);

                // if transaction amount has went negative then the debit/credit indicator must change
                if (transaction.TransactionAmount < 0)
                {
                    transaction.TransactionAmount = Math.Abs(transaction.TransactionAmount);
                    transaction.AmountInBaseCurrency = Math.Abs(transaction.AmountInBaseCurrency);
                    transaction.AmountInIntlCurrency = Math.Abs(transaction.AmountInIntlCurrency);

                    transaction.DebitCreditIndicator = !transaction.DebitCreditIndicator;
                }
            }

            if (transaction.TransactionAmount == 0)
            {
                transaction.Delete();
            }
        }

        private static void LoadGiftDonorRelatedData(GiftBatchTDS AGiftDS,
            bool ARecurring,
            Int32 ALedgerNumber,
            Int32 ABatchNumber,
            ref TDBTransaction ATransaction)
        {
            // load all donor shortnames in one go
            string getDonorSQL =
                "SELECT DISTINCT dp.p_partner_key_n, dp.p_partner_short_name_c, dp.p_status_code_c FROM PUB_p_partner dp, PUB_a_gift g " + //, dp.p_receipt_each_gift_l
                "WHERE g.a_ledger_number_i = ? AND g.a_batch_number_i = ? AND g.p_donor_key_n = dp.p_partner_key_n";

            if (ARecurring)
            {
                getDonorSQL = getDonorSQL.Replace("PUB_a_gift", "PUB_a_recurring_gift");
            }

            List <OdbcParameter>parameters = new List <OdbcParameter>();
            OdbcParameter param = new OdbcParameter("ledger", OdbcType.Int);
            param.Value = ALedgerNumber;
            parameters.Add(param);
            param = new OdbcParameter("batch", OdbcType.Int);
            param.Value = ABatchNumber;
            parameters.Add(param);

            DBAccess.GDBAccessObj.Select(AGiftDS, getDonorSQL, AGiftDS.DonorPartners.TableName,
                ATransaction,
                parameters.ToArray(), 0, 0);

            // load all recipient partners and fields related to this gift batch in one go
            string getRecipientSQL =
                "SELECT DISTINCT rp.* FROM PUB_p_partner rp, PUB_a_gift_detail gd " +
                "WHERE gd.a_ledger_number_i = ? AND gd.a_batch_number_i = ? AND gd.p_recipient_key_n = rp.p_partner_key_n";

            if (ARecurring)
            {
                getRecipientSQL = getRecipientSQL.Replace("PUB_a_gift", "PUB_a_recurring_gift");
            }

            DBAccess.GDBAccessObj.Select(AGiftDS, getRecipientSQL, AGiftDS.RecipientPartners.TableName,
                ATransaction,
                parameters.ToArray(), 0, 0);

            string getRecipientFamilySQL =
                "SELECT DISTINCT pf.* FROM PUB_p_family pf, PUB_a_gift_detail gd " +
                "WHERE gd.a_ledger_number_i = ? AND gd.a_batch_number_i = ? AND gd.p_recipient_key_n = pf.p_partner_key_n";

            if (ARecurring)
            {
                getRecipientFamilySQL = getRecipientFamilySQL.Replace("PUB_a_gift", "PUB_a_recurring_gift");
            }

            DBAccess.GDBAccessObj.Select(AGiftDS, getRecipientFamilySQL, AGiftDS.RecipientFamily.TableName,
                ATransaction,
                parameters.ToArray(), 0, 0);

            string getRecipientPersonSQL =
                "SELECT DISTINCT pf.* FROM PUB_p_Person pf, PUB_a_gift_detail gd " +
                "WHERE gd.a_ledger_number_i = ? AND gd.a_batch_number_i = ? AND gd.p_recipient_key_n = pf.p_partner_key_n";

            if (ARecurring)
            {
                getRecipientPersonSQL = getRecipientPersonSQL.Replace("PUB_a_gift", "PUB_a_recurring_gift");
            }

            DBAccess.GDBAccessObj.Select(AGiftDS, getRecipientPersonSQL, AGiftDS.RecipientPerson.TableName,
                ATransaction,
                parameters.ToArray(), 0, 0);

            string getRecipientUnitSQL =
                "SELECT DISTINCT pf.* FROM PUB_p_Unit pf, PUB_a_gift_detail gd " +
                "WHERE gd.a_ledger_number_i = ? AND gd.a_batch_number_i = ? AND gd.p_recipient_key_n = pf.p_partner_key_n";

            if (ARecurring)
            {
                getRecipientUnitSQL = getRecipientUnitSQL.Replace("PUB_a_gift", "PUB_a_recurring_gift");
            }

            DBAccess.GDBAccessObj.Select(AGiftDS, getRecipientUnitSQL, AGiftDS.RecipientUnit.TableName,
                ATransaction,
                parameters.ToArray(), 0, 0);
        }

        /// <summary>
        /// Check if an entry exists in ValidLedgerNumber for the specified ledger number and partner key
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="APartnerKey"></param>
        /// <param name="ACostCentreCode"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static bool CheckCostCentreLinkForRecipient(Int32 ALedgerNumber, Int64 APartnerKey, out string ACostCentreCode)
        {
            bool CostCentreExists = false;

            ACostCentreCode = string.Empty;
            string CostCentreCode = ACostCentreCode;

            string ValidLedgerNumberTable = "ValidLedgerNumber";

            string GetPartnerValidLedgerNumberSQL = String.Format("SELECT DISTINCT vln.a_cost_centre_code_c FROM PUB_a_valid_ledger_number vln " +
                "WHERE vln.a_ledger_number_i = {0} AND vln.p_partner_key_n = {1}",
                ALedgerNumber,
                APartnerKey);

            DataSet tempDataSet = new DataSet();

            TDBTransaction Transaction = null;
            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    try
                    {
                        DBAccess.GDBAccessObj.Select(tempDataSet, GetPartnerValidLedgerNumberSQL, ValidLedgerNumberTable,
                            Transaction,
                            0, 0);

                        if (tempDataSet.Tables[ValidLedgerNumberTable] != null)
                        {
                            if (tempDataSet.Tables[ValidLedgerNumberTable].Rows.Count > 0)
                            {
                                DataRow row = tempDataSet.Tables[ValidLedgerNumberTable].Rows[0];
                                CostCentreCode = row[0].ToString();
                                CostCentreExists = true;
                            }

                            tempDataSet.Clear();
                        }
                    }
                    catch (Exception e)
                    {
                        TLogging.Log("Error in CheckCostCentreLinkForRecipient: " + e.Message);
                    }
                });

            ACostCentreCode = CostCentreCode;

            return CostCentreExists;
        }

        /// <summary>
        /// Get gift destination for recipient
        /// </summary>
        /// <param name="APartnerKey"></param>
        /// <param name="AGiftDate"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static Int64 GetGiftDestinationForRecipient(Int64 APartnerKey, DateTime ? AGiftDate)
        {
            Int64 PartnerField = 0;

            if ((APartnerKey == 0) || !AGiftDate.HasValue)
            {
                return 0;
            }

            string GetPartnerGiftDestinationSQL = String.Format("SELECT DISTINCT pgd.p_field_key_n as FieldKey" +
                "  FROM PUB_p_partner_gift_destination pgd " +
                "  WHERE pgd.p_partner_key_n = {0}" +
                "    And ((pgd.p_date_effective_d <= '{1:yyyy-MM-dd}' And pgd.p_date_expires_d IS NULL)" +
                "         Or ('{1:yyyy-MM-dd}' BETWEEN pgd.p_date_effective_d And pgd.p_date_expires_d))",
                APartnerKey,
                AGiftDate);

            DataTable GiftDestTable = null;
            string PartnerGiftDestinationTable = "PartnerGiftDestination";

            TDBTransaction Transaction = null;
            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    try
                    {
                        GiftDestTable = DBAccess.GDBAccessObj.SelectDT(GetPartnerGiftDestinationSQL, PartnerGiftDestinationTable, Transaction);

                        if ((GiftDestTable != null) && (GiftDestTable.Rows.Count > 0))
                        {
                            PartnerField = (Int64)GiftDestTable.DefaultView[0].Row["FieldKey"];
                        }
                    }
                    catch (Exception e)
                    {
                        TLogging.Log("Error in GetGiftDestinationForRecipient: " + e.Message);
                    }
                });

            return PartnerField;
        }

        /// <summary>
        /// Check if an entry exists in ValidLedgerNumber for the specified ledger number and partner key
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="APartnerKey"></param>
        /// <param name="ACostCentreCode"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static bool CheckCostCentreDestinationForRecipient(Int32 ALedgerNumber,
            Int64 APartnerKey,
            out string ACostCentreCode)
        {
            bool CostCentreExists = false;
            string CostCentreCodeTableName = "CostCentreCodes";

            ACostCentreCode = string.Empty;
            string CostCentreCode = ACostCentreCode;

            string GetCostCentreCodeSQL = String.Format(
                "SELECT a_cost_centre_code_c as CostCentreCode FROM public.a_valid_ledger_number WHERE a_ledger_number_i = {0} AND p_partner_key_n = {1};",
                ALedgerNumber,
                APartnerKey
                );

            DataTable CostCentreCodesTbl = null;

            TDBTransaction Transaction = null;
            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    CostCentreCodesTbl = DBAccess.GDBAccessObj.SelectDT(GetCostCentreCodeSQL, CostCentreCodeTableName, Transaction);

                    if ((CostCentreCodesTbl != null) && (CostCentreCodesTbl.Rows.Count > 0))
                    {
                        CostCentreCode = (string)CostCentreCodesTbl.DefaultView[0].Row["CostCentreCode"];
                        CostCentreExists = true;
                    }
                });

            ACostCentreCode = CostCentreCode;

            return CostCentreExists;
        }

        /// <summary>
        /// Create GiftBatchTDS with the gift batch to post, and all gift transactions and details, and motivation details
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AExcludeBatchRow"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadAGiftBatchAndRelatedData(Int32 ALedgerNumber, Int32 ABatchNumber, bool AExcludeBatchRow = false)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            bool ChangesToCommit = false;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    MainDS = LoadAGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber, Transaction, out ChangesToCommit, AExcludeBatchRow);
                });

            if (ChangesToCommit)
            {
                GiftBatchTDSAccess.SubmitChanges(MainDS);
            }

            return MainDS;
        }

        /// <summary>
        /// Create GiftBatchTDS with the gift batch to post, and all gift transactions and details, and motivation details
        /// </summary>
        [RequireModulePermission("FINANCE-1")]
        private static GiftBatchTDS LoadAGiftBatchAndRelatedData(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            TDBTransaction ATransaction,
            out bool AChangesToCommit,
            bool AExcludeBatchRow = false)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            AChangesToCommit = false;

            bool TaxDeductiblePercentageEnabled = Convert.ToBoolean(
                TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_TAXDEDUCTIBLEPERCENTAGE, "FALSE"));

            try
            {
                ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ATransaction);
                AMotivationDetailAccess.LoadViaALedger(MainDS, ALedgerNumber, ATransaction);
                AGiftBatchAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, ATransaction);
                AGiftAccess.LoadViaAGiftBatch(MainDS, ALedgerNumber, ABatchNumber, ATransaction);
                AGiftDetailAccess.LoadViaAGiftBatch(MainDS, ALedgerNumber, ABatchNumber, ATransaction);

                //Load Ledger Partner types
                MainDS.LedgerPartnerTypes.Merge(PPartnerTypeAccess.LoadViaPType(MPartnerConstants.PARTNERTYPE_LEDGER, ATransaction));

                //Load related donor data
                LoadGiftDonorRelatedData(MainDS, false, ALedgerNumber, ABatchNumber, ref ATransaction);

                DataView giftView = new DataView(MainDS.AGift);
                giftView.Sort = AGiftTable.GetGiftTransactionNumberDBName();

                bool allowUpdates = (MainDS.AGiftBatch[0].BatchStatus == MFinanceConstants.BATCH_UNPOSTED);

                // fill the columns in the modified GiftDetail Table to show donorkey, dateentered etc in the grid
                foreach (GiftBatchTDSAGiftDetailRow giftDetail in MainDS.AGiftDetail.Rows)
                {
                    // get the gift
                    AGiftRow giftRow = (AGiftRow)giftView.FindRows(giftDetail.GiftTransactionNumber)[0].Row;

                    PPartnerRow DonorRow = (PPartnerRow)MainDS.DonorPartners.Rows.Find(giftRow.DonorKey);

                    AMotivationDetailRow motivationDetailRow = (AMotivationDetailRow)MainDS.AMotivationDetail.Rows.Find(
                        new object[] { ALedgerNumber, giftDetail.MotivationGroupCode, giftDetail.MotivationDetailCode });

                    giftDetail.DonorKey = giftRow.DonorKey;
                    giftDetail.DonorName = DonorRow.PartnerShortName;
                    giftDetail.DonorClass = DonorRow.PartnerClass;
                    giftDetail.MethodOfGivingCode = giftRow.MethodOfGivingCode;
                    giftDetail.MethodOfPaymentCode = giftRow.MethodOfPaymentCode;
                    giftDetail.ReceiptNumber = giftRow.ReceiptNumber;
                    giftDetail.ReceiptPrinted = giftRow.ReceiptPrinted;
                    giftDetail.DateEntered = giftRow.DateEntered;
                    giftDetail.Reference = giftRow.Reference;

                    //do the same for the Recipient
                    if (giftDetail.RecipientKey > 0)
                    {
                        // if true then this gift is protected and data cannot be changed
                        // (note: here this includes all negative gifts and not just reversals)
                        if (allowUpdates && (giftDetail.GiftTransactionAmount > 0))
                        {
                            // get the current Recipient Fund Number
                            giftDetail.RecipientField = GetRecipientFundNumberInner(MainDS, giftDetail.RecipientKey, giftDetail.DateEntered);

                            // these will be different if the recipient fund number has changed (i.e. a changed Gift Destination)
                            if (giftDetail.RecipientLedgerNumber != giftDetail.RecipientField)
                            {
                                giftDetail.RecipientLedgerNumber = giftDetail.RecipientField;
                                AChangesToCommit = true;
                            }

                            try
                            {
                                string newCostCentreCode =
                                    RetrieveCostCentreCodeForRecipient(ALedgerNumber,
                                        giftDetail.RecipientKey,
                                        giftDetail.RecipientLedgerNumber,
                                        giftDetail.DateEntered,
                                        motivationDetailRow.MotivationGroupCode,
                                        motivationDetailRow.MotivationDetailCode,
                                        ATransaction);

                                if (giftDetail.CostCentreCode != newCostCentreCode)
                                {
                                    giftDetail.CostCentreCode = newCostCentreCode;
                                    AChangesToCommit = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                TLogging.Log("Error in: LoadAGiftBatchAndRelatedData - " + ex.Message);
                            }
                        }
                        else
                        {
                            giftDetail.RecipientField = giftDetail.RecipientLedgerNumber;
                        }

                        PPartnerRow RecipientRow = (PPartnerRow)MainDS.RecipientPartners.Rows.Find(giftDetail.RecipientKey);

                        if (RecipientRow != null)
                        {
                            giftDetail.RecipientDescription = RecipientRow.PartnerShortName;
                        }
                        else
                        {
                            giftDetail.RecipientDescription = "INVALID";
                        }

                        PUnitRow RecipientUnitRow = (PUnitRow)MainDS.RecipientUnit.Rows.Find(giftDetail.RecipientKey);

                        if ((RecipientUnitRow != null) && (RecipientUnitRow.UnitTypeCode == MPartnerConstants.UNIT_TYPE_KEYMIN))
                        {
                            giftDetail.RecipientKeyMinistry = RecipientUnitRow.UnitName;
                        }
                        else
                        {
                            giftDetail.SetRecipientKeyMinistryNull();
                        }

                        if (TaxDeductiblePercentageEnabled)
                        {
                            MainDS.PPartnerTaxDeductiblePct.Merge(
                                PPartnerTaxDeductiblePctAccess.LoadViaPPartner(giftDetail.RecipientKey, ATransaction));
                        }
                    }
                    else
                    {
                        giftDetail.RecipientDescription = "INVALID";
                        giftDetail.SetRecipientFieldNull();
                        giftDetail.SetRecipientKeyMinistryNull();
                    }

                    //And account code
                    if (motivationDetailRow != null)
                    {
                        giftDetail.AccountCode = motivationDetailRow.AccountCode;
                        giftDetail.TaxDeductibleAccountCode = motivationDetailRow.TaxDeductibleAccount;
                    }
                    else
                    {
                        giftDetail.SetAccountCodeNull();
                        giftDetail.SetTaxDeductibleAccountCodeNull();
                    }
                }
            }
            catch (Exception e)
            {
                TLogging.Log("Error in LoadAGiftBatchAndRelatedData with Trans: " + e.Message);
                throw;
            }

            if (AExcludeBatchRow)
            {
                MainDS.AGiftBatch.Clear();
            }

            return MainDS;
        }

        /// <summary>
        /// Create GiftBatchTDS with the gift batch to post, and all gift transactions and details, and motivation details
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AExcludeBatchRow"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static GiftBatchTDS LoadARecurringGiftBatchAndRelatedData(Int32 ALedgerNumber, Int32 ABatchNumber, bool AExcludeBatchRow = false)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();

            bool ChangesToCommit = false;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    MainDS = LoadARecurringGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber, Transaction, out ChangesToCommit, AExcludeBatchRow);
                });

            if (ChangesToCommit)
            {
                // if RecipientLedgerNumber has been updated then this should immediately be saved to the database
                GiftBatchTDSAccess.SubmitChanges(MainDS);
            }

            return MainDS;
        }

        /// <summary>
        /// create GiftBatchTDS with the recurring gift batch, and all gift transactions and details, and motivation details
        /// </summary>
        [RequireModulePermission("FINANCE-1")]
        private static GiftBatchTDS LoadARecurringGiftBatchAndRelatedData(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            TDBTransaction ATransaction,
            out bool AChangesToCommit,
            bool AExcludeBatchRow = false)
        {
            AChangesToCommit = false;

            GiftBatchTDS MainDS = new GiftBatchTDS();

            ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ATransaction);
            ARecurringGiftBatchAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, ATransaction);
            ARecurringGiftAccess.LoadViaARecurringGiftBatch(MainDS, ALedgerNumber, ABatchNumber, ATransaction);
            ARecurringGiftDetailAccess.LoadViaARecurringGiftBatch(MainDS, ALedgerNumber, ABatchNumber, ATransaction);
            AMotivationDetailAccess.LoadViaALedger(MainDS, ALedgerNumber, ATransaction);

            LoadGiftDonorRelatedData(MainDS, true, ALedgerNumber, ABatchNumber, ref ATransaction);

            DataView giftView = new DataView(MainDS.ARecurringGift);
            giftView.Sort = ARecurringGiftTable.GetGiftTransactionNumberDBName();

            // fill the columns in the modified GiftDetail Table to show donorkey, dateentered etc in the grid
            foreach (GiftBatchTDSARecurringGiftDetailRow giftDetail in MainDS.ARecurringGiftDetail.Rows)
            {
                // get the gift
                ARecurringGiftRow giftRow = (ARecurringGiftRow)giftView.FindRows(giftDetail.GiftTransactionNumber)[0].Row;

                PPartnerRow DonorRow = (PPartnerRow)MainDS.DonorPartners.Rows.Find(giftRow.DonorKey);

                giftDetail.DonorKey = giftRow.DonorKey;
                giftDetail.DonorName = DonorRow.PartnerShortName;
                giftDetail.DonorClass = DonorRow.PartnerClass;
                giftDetail.MethodOfGivingCode = giftRow.MethodOfGivingCode;
                giftDetail.MethodOfPaymentCode = giftRow.MethodOfPaymentCode;

                AMotivationDetailRow motivationDetailRow = (AMotivationDetailRow)MainDS.AMotivationDetail.Rows.Find(
                    new object[] { ALedgerNumber, giftDetail.MotivationGroupCode, giftDetail.MotivationDetailCode });

                //do the same for the Recipient
                if (giftDetail.RecipientKey > 0)
                {
                    // GiftAmount should never be negative. Negative Recurring gifts are not allowed!
                    if (giftDetail.GiftAmount < 0)
                    {
                        giftDetail.RecipientField = giftDetail.RecipientLedgerNumber;
                    }
                    else
                    {
                        // get the current Recipient Fund Number
                        giftDetail.RecipientField = GetRecipientFundNumberInner(MainDS, giftDetail.RecipientKey, DateTime.Today);

                        // these will be different if the recipient fund number has changed (i.e. a changed Gift Destination)
                        if (giftDetail.RecipientLedgerNumber != giftDetail.RecipientField)
                        {
                            giftDetail.RecipientLedgerNumber = giftDetail.RecipientField;
                            AChangesToCommit = true;
                        }

                        string newCostCentreCode =
                            RetrieveCostCentreCodeForRecipient(ALedgerNumber, giftDetail.RecipientKey, giftDetail.RecipientLedgerNumber,
                                giftDetail.DateEntered, motivationDetailRow.MotivationGroupCode, motivationDetailRow.MotivationDetailCode);

                        if (giftDetail.CostCentreCode != newCostCentreCode)
                        {
                            giftDetail.CostCentreCode = newCostCentreCode;
                            AChangesToCommit = true;
                        }
                    }

                    PPartnerRow RecipientRow = (PPartnerRow)MainDS.RecipientPartners.Rows.Find(giftDetail.RecipientKey);
                    giftDetail.RecipientDescription = RecipientRow.PartnerShortName;

                    PUnitRow RecipientUnitRow = (PUnitRow)MainDS.RecipientUnit.Rows.Find(giftDetail.RecipientKey);

                    if ((RecipientUnitRow != null) && (RecipientUnitRow.UnitTypeCode == MPartnerConstants.UNIT_TYPE_KEYMIN))
                    {
                        giftDetail.RecipientKeyMinistry = RecipientUnitRow.UnitName;
                    }
                    else
                    {
                        giftDetail.SetRecipientKeyMinistryNull();
                    }
                }
                else
                {
                    giftDetail.SetRecipientFieldNull();
                    giftDetail.RecipientDescription = "INVALID";
                    giftDetail.SetRecipientKeyMinistryNull();
                }

                //And account code
                if (motivationDetailRow != null)
                {
                    giftDetail.AccountCode = motivationDetailRow.AccountCode.ToString();
                }
                else
                {
                    giftDetail.SetAccountCodeNull();
                }
            }

            if (AExcludeBatchRow)
            {
                MainDS.ARecurringGiftBatch.Clear();
            }

            return MainDS;
        }

        /// <summary>
        /// calculate the admin fee for a given amount.
        /// public so that it can be tested by NUnit tests.
        /// </summary>
        /// <param name="MainDS"></param>
        /// <param name="ALedgerNumber"></param>
        /// <param name="AFeeCode"></param>
        /// <param name="AGiftAmount"></param>
        /// <param name="AVerificationResult"></param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-3")]
        public static decimal CalculateAdminFee(GiftBatchTDS MainDS,
            Int32 ALedgerNumber,
            string AFeeCode,
            decimal AGiftAmount,
            out TVerificationResultCollection AVerificationResult
            )
        {
            //Amount to return
            decimal FeeAmount = 0;

            decimal GiftPercentageAmount;
            decimal ChargeAmount;
            string ChargeOption;

            //Error handling
            string ErrorContext = String.Empty;
            string ErrorMessage = String.Empty;
            //Set default type as non-critical
            TResultSeverity ErrorType = TResultSeverity.Resv_Noncritical;

            AVerificationResult = new TVerificationResultCollection();

            try
            {
                AFeesPayableRow feePayableRow = (AFeesPayableRow)MainDS.AFeesPayable.Rows.Find(new object[] { ALedgerNumber, AFeeCode });

                if (feePayableRow == null)
                {
                    AFeesReceivableRow feeReceivableRow = (AFeesReceivableRow)MainDS.AFeesReceivable.Rows.Find(new object[] { ALedgerNumber, AFeeCode });

                    if (feeReceivableRow == null)
                    {
                        ErrorContext = "Calculate Admin Fee";
                        ErrorMessage = String.Format(Catalog.GetString("The Ledger no.: {0} or Fee Code: {1} does not exist."),
                            ALedgerNumber,
                            AFeeCode
                            );
                        ErrorType = TResultSeverity.Resv_Noncritical;
                        throw new System.ArgumentException(ErrorMessage);
                    }
                    else
                    {
                        GiftPercentageAmount = feeReceivableRow.ChargePercentage * AGiftAmount / 100;
                        ChargeOption = feeReceivableRow.ChargeOption.ToUpper();
                        ChargeAmount = feeReceivableRow.ChargeAmount;
                    }
                }
                else
                {
                    GiftPercentageAmount = feePayableRow.ChargePercentage * AGiftAmount / 100;
                    ChargeOption = feePayableRow.ChargeOption.ToUpper();
                    ChargeAmount = feePayableRow.ChargeAmount;
                }

                switch (ChargeOption)
                {
                    case MFinanceConstants.ADMIN_CHARGE_OPTION_FIXED :

                        if (AGiftAmount >= 0)
                        {
                            FeeAmount = ChargeAmount;
                        }
                        else
                        {
                            FeeAmount = -ChargeAmount;
                        }

                        break;

                    case MFinanceConstants.ADMIN_CHARGE_OPTION_MIN:

                        if (AGiftAmount >= 0)
                        {
                            if (ChargeAmount >= GiftPercentageAmount)
                            {
                                FeeAmount = ChargeAmount;
                            }
                            else
                            {
                                FeeAmount = GiftPercentageAmount;
                            }
                        }
                        else
                        {
                            if (-ChargeAmount <= GiftPercentageAmount)
                            {
                                FeeAmount = -ChargeAmount;
                            }
                            else
                            {
                                FeeAmount = GiftPercentageAmount;
                            }
                        }

                        break;

                    case MFinanceConstants.ADMIN_CHARGE_OPTION_MAX:

                        if (AGiftAmount >= 0)
                        {
                            if (ChargeAmount <= GiftPercentageAmount)
                            {
                                FeeAmount = ChargeAmount;
                            }
                            else
                            {
                                FeeAmount = GiftPercentageAmount;
                            }
                        }
                        else
                        {
                            if (-ChargeAmount >= GiftPercentageAmount)
                            {
                                FeeAmount = -ChargeAmount;
                            }
                            else
                            {
                                FeeAmount = GiftPercentageAmount;
                            }
                        }

                        break;

                    case MFinanceConstants.ADMIN_CHARGE_OPTION_PERCENT:
                        FeeAmount = GiftPercentageAmount;
                        break;

                    default:
                        ErrorContext = "Calculate Admin Fee";
                        ErrorMessage =
                            String.Format(Catalog.GetString("Unexpected Fee Payable/Receivable Charge Option in Ledger: {0} and Fee Code: '{1}'."),
                                ALedgerNumber,
                                AFeeCode
                                );
                        ErrorType = TResultSeverity.Resv_Noncritical;
                        throw new System.InvalidOperationException(ErrorMessage);
                }
            }
            catch (ArgumentException ex)
            {
                AVerificationResult.Add(new TVerificationResult(ErrorContext, ex.Message, ErrorType));
            }
            catch (InvalidOperationException ex)
            {
                AVerificationResult.Add(new TVerificationResult(ErrorContext, ex.Message, ErrorType));
            }
            catch (Exception ex)
            {
                ErrorContext = "Calculate Admin Fee";
                ErrorMessage = String.Format(Catalog.GetString("Unknown error while calculating admin fee for Ledger: {0} and Fee Code: {1}" +
                        Environment.NewLine + Environment.NewLine + ex.ToString()),
                    ALedgerNumber,
                    AFeeCode
                    );
                ErrorType = TResultSeverity.Resv_Critical;
                AVerificationResult.Add(new TVerificationResult(ErrorContext, ErrorMessage, ErrorType));
            }

            // calculate the admin fee for the specific amount and admin fee. see gl4391.p

            return FeeAmount;
        }

        private static void AddToFeeTotals(GiftBatchTDS AMainDS,
            AGiftDetailRow AGiftDetailRow,
            string AFeeCode,
            decimal AFeeAmount,
            int APostingPeriod)
        {
            // TODO CT
            // see Add_To_Fee_Totals in gr1210.p

            try
            {
                /* Get the record for the totals of the processed fees. */
                AProcessedFeeTable ProcessedFeeDataTable = AMainDS.AProcessedFee;
                AProcessedFeeRow ProcessedFeeRow =
                    (AProcessedFeeRow)ProcessedFeeDataTable.Rows.Find(new object[] { AGiftDetailRow.LedgerNumber,
                                                                                     AGiftDetailRow.BatchNumber,
                                                                                     AGiftDetailRow.GiftTransactionNumber,
                                                                                     AGiftDetailRow.DetailNumber,
                                                                                     AFeeCode });

                if (ProcessedFeeRow == null)
                {
                    ProcessedFeeRow = (AProcessedFeeRow)ProcessedFeeDataTable.NewRowTyped(false);
                    ProcessedFeeRow.LedgerNumber = AGiftDetailRow.LedgerNumber;
                    ProcessedFeeRow.BatchNumber = AGiftDetailRow.BatchNumber;
                    ProcessedFeeRow.GiftTransactionNumber = AGiftDetailRow.GiftTransactionNumber;
                    ProcessedFeeRow.DetailNumber = AGiftDetailRow.DetailNumber;
                    ProcessedFeeRow.FeeCode = AFeeCode;
                    ProcessedFeeRow.PeriodicAmount = 0;

                    ProcessedFeeDataTable.Rows.Add(ProcessedFeeRow);
                }

                ProcessedFeeRow.CostCentreCode = AGiftDetailRow.CostCentreCode;
                ProcessedFeeRow.PeriodNumber = APostingPeriod;

                /* Add the amount to the existing total. */
                ProcessedFeeRow.PeriodicAmount += AFeeAmount;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static GiftBatchTDS PrepareGiftBatchForPosting(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            ref TDBTransaction ATransaction,
            out TVerificationResultCollection AVerifications)
        {
            GiftBatchTDS MainDS = LoadAGiftBatchSingle(ALedgerNumber, ABatchNumber, ref ATransaction);

            string LedgerBaseCurrency = MainDS.ALedger[0].BaseCurrency;
            string LedgerIntlCurrency = MainDS.ALedger[0].IntlCurrency;

            AVerifications = new TVerificationResultCollection();

            if ((MainDS.AGiftBatch == null) || (MainDS.AGiftBatch.Rows.Count < 1))
            {
                AVerifications.Add(
                    new TVerificationResult(
                        "Posting Gift Batch",
                        String.Format("Unable to Load GiftBatchData ({0}, {1})",
                            ALedgerNumber,
                            ABatchNumber),
                        TResultSeverity.Resv_Critical));
                return null;
            }
            else if (MainDS.AGiftBatch[0].BatchStatus != MFinanceConstants.BATCH_UNPOSTED)
            {
                AVerifications.Add(
                    new TVerificationResult(
                        "Posting Gift Batch",
                        String.Format("Cannot post batch ({0}, {1}) with status: {2}",
                            ALedgerNumber,
                            ABatchNumber,
                            MainDS.AGiftBatch[0].BatchStatus),
                        TResultSeverity.Resv_Critical));
                return null;
            }

            //Load all other related data for the batch and commit any changes
            bool ChangesToCommit = false;
            MainDS.Merge(LoadAGiftBatchAndRelatedData(ALedgerNumber, ABatchNumber, ATransaction, out ChangesToCommit, true));

            if (ChangesToCommit)
            {
                GiftBatchTDSAccess.SubmitChanges(MainDS);
            }

            AGiftBatchRow GiftBatchRow = MainDS.AGiftBatch[0];

            string BatchTransactionCurrency = GiftBatchRow.CurrencyCode;

            // for calculation of admin fees
            LoadAdminFeeTablesForGiftBatch(MainDS, ALedgerNumber, ABatchNumber, ATransaction);

            // check that the Gift Batch BatchPeriod matches the date effective
            DateTime GLEffectiveDate = GiftBatchRow.GlEffectiveDate;
            DateTime StartOfMonth = new DateTime(GLEffectiveDate.Year, GLEffectiveDate.Month, 1);
            int DateEffectivePeriod, DateEffectiveYear;

            TFinancialYear.IsValidPostingPeriod(GiftBatchRow.LedgerNumber,
                GiftBatchRow.GlEffectiveDate,
                out DateEffectivePeriod,
                out DateEffectiveYear,
                ATransaction);

            decimal IntlToBaseExchRate = TExchangeRateTools.GetCorporateExchangeRate(LedgerBaseCurrency,
                LedgerIntlCurrency,
                StartOfMonth,
                GLEffectiveDate);

            if (GiftBatchRow.BatchPeriod != DateEffectivePeriod)
            {
                AVerifications.Add(
                    new TVerificationResult(
                        "Posting Gift Batch",
                        String.Format("Invalid gift batch period {0} for date {1}",
                            GiftBatchRow.BatchPeriod,
                            GLEffectiveDate),
                        TResultSeverity.Resv_Critical));
                return null;
            }
            else if (IntlToBaseExchRate == 0)
            {
                AVerifications.Add(
                    new TVerificationResult(
                        "Posting Gift Batch",
                        String.Format(Catalog.GetString("No Corporate Exchange rate exists for the month: {0:MMMM yyyy}!"),
                            GLEffectiveDate),
                        TResultSeverity.Resv_Critical));
                return null;
            }
            else if ((GiftBatchRow.HashTotal != 0) && (GiftBatchRow.BatchTotal != GiftBatchRow.HashTotal))
            {
                AVerifications.Add(
                    new TVerificationResult(
                        "Posting Gift Batch",
                        String.Format("The gift batch total ({0}) does not equal the hash total ({1}).",
                            StringHelper.FormatUsingCurrencyCode(GiftBatchRow.BatchTotal, GiftBatchRow.CurrencyCode),
                            StringHelper.FormatUsingCurrencyCode(GiftBatchRow.HashTotal, GiftBatchRow.CurrencyCode)),
                        TResultSeverity.Resv_Critical));
                return null;
            }

            //Check validity at the gift detail level
            foreach (GiftBatchTDSAGiftDetailRow giftDetail in MainDS.AGiftDetail.Rows)
            {
                // do not allow posting gifts with no donor
                if (giftDetail.DonorKey == 0)
                {
                    AVerifications.Add(
                        new TVerificationResult(
                            "Posting Gift Batch",
                            String.Format(Catalog.GetString("Donor Key needed in gift {0}"),
                                giftDetail.GiftTransactionNumber),
                            TResultSeverity.Resv_Critical));
                    return null;
                }

                // find motivation detail
                AMotivationDetailRow motivationRow =
                    (AMotivationDetailRow)MainDS.AMotivationDetail.Rows.Find(new object[] { ALedgerNumber,
                                                                                            giftDetail.MotivationGroupCode,
                                                                                            giftDetail.MotivationDetailCode });

                if (motivationRow == null)
                {
                    AVerifications.Add(
                        new TVerificationResult(
                            "Posting Gift Batch",
                            String.Format("Invalid motivation detail {0}/{1} in gift {2}",
                                giftDetail.MotivationGroupCode,
                                giftDetail.MotivationDetailCode,
                                giftDetail.GiftTransactionNumber),
                            TResultSeverity.Resv_Critical));
                    return null;
                }

                // data is only updated if the gift amount is positive
                if (giftDetail.GiftTransactionAmount >= 0)
                {
                    // The recipient ledger number must not be 0 if the motivation group is 'GIFT'
                    if ((giftDetail.IsRecipientLedgerNumberNull() || (giftDetail.RecipientLedgerNumber == 0))
                        && (giftDetail.MotivationGroupCode == MFinanceConstants.MOTIVATION_GROUP_GIFT))
                    {
                        AVerifications.Add(
                            new TVerificationResult(
                                "Posting Gift Batch",
                                String.Format(Catalog.GetString("No valid Gift Destination exists for the recipient {0} ({1}) of gift {2}."),
                                    giftDetail.RecipientDescription,
                                    giftDetail.RecipientKey,
                                    giftDetail.GiftTransactionNumber) +
                                "\n\n" +
                                Catalog.GetString(
                                    "A Gift Destination will need to be assigned to this Partner before this gift can be posted with the Motivation Group 'GIFT'."),
                                TResultSeverity.Resv_Critical));
                        return null;
                    }

                    if (giftDetail.IsCostCentreCodeNull() || (giftDetail.CostCentreCode == string.Empty))
                    {
                        AVerifications.Add(
                            new TVerificationResult(
                                "Posting Gift Batch",
                                String.Format(Catalog.GetString("No valid Cost Centre Code exists for the recipient {0} ({1}) of gift {2}."),
                                    giftDetail.RecipientDescription,
                                    giftDetail.RecipientKey,
                                    giftDetail.GiftTransactionNumber) +
                                "\n\n" +
                                Catalog.GetString(
                                    "A Gift Destination will need to be assigned to this Partner."),
                                TResultSeverity.Resv_Critical));
                        return null;
                    }
                }

                // set column giftdetail.AccountCode motivation
                giftDetail.AccountCode = motivationRow.AccountCode;

                giftDetail.GiftAmount = giftDetail.GiftTransactionAmount / GiftBatchRow.ExchangeRateToBase;

                if (BatchTransactionCurrency != LedgerIntlCurrency)
                {
                    giftDetail.GiftAmountIntl = giftDetail.GiftAmount / IntlToBaseExchRate;
                }
                else
                {
                    giftDetail.GiftAmountIntl = giftDetail.GiftTransactionAmount;
                }

                // get all motivation detail fees for this gift
                foreach (AMotivationDetailFeeRow motivationFeeRow in MainDS.AMotivationDetailFee.Rows)
                {
                    if ((motivationFeeRow.MotivationDetailCode == motivationRow.MotivationDetailCode)
                        && (motivationFeeRow.MotivationGroupCode == motivationRow.MotivationGroupCode))
                    {
                        TVerificationResultCollection Verifications2;

                        decimal FeeAmount = CalculateAdminFee(MainDS,
                            ALedgerNumber,
                            motivationFeeRow.FeeCode,
                            giftDetail.GiftAmount,
                            out Verifications2);

                        if (!TVerificationHelper.IsNullOrOnlyNonCritical(Verifications2))
                        {
                            AVerifications.AddCollection(Verifications2);

                            return null;
                        }

                        AddToFeeTotals(MainDS, giftDetail, motivationFeeRow.FeeCode, FeeAmount, GiftBatchRow.BatchPeriod);
                    }
                }
            }

            return MainDS;
        }

        private static void LoadAdminFeeTablesForGiftBatch(GiftBatchTDS AMainDS,
            Int32 ALedgerNumber,
            Int32 ABatchNumber,
            TDBTransaction ATransaction)
        {
            // for calculation of admin fees
            AMotivationDetailFeeAccess.LoadViaALedger(AMainDS, ALedgerNumber, ATransaction);
            AFeesPayableAccess.LoadViaALedger(AMainDS, ALedgerNumber, ATransaction);
            AFeesReceivableAccess.LoadViaALedger(AMainDS, ALedgerNumber, ATransaction);
            AProcessedFeeAccess.LoadViaAGiftBatch(AMainDS, ALedgerNumber, ABatchNumber, ATransaction);
        }

        /// <summary>
        /// post a Gift Batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AVerifications"></param>
        [RequireModulePermission("FINANCE-2")]
        public static bool PostGiftBatch(Int32 ALedgerNumber, Int32 ABatchNumber, out TVerificationResultCollection AVerifications)
        {
            List <Int32>GiftBatches = new List <int>();
            GiftBatches.Add(ABatchNumber);

            return PostGiftBatches(ALedgerNumber, GiftBatches, out AVerifications);
        }

        /// <summary>
        /// post several gift batches at once
        /// </summary>
        [RequireModulePermission("FINANCE-2")]
        public static bool PostGiftBatches(Int32 ALedgerNumber, List <Int32>ABatchNumbers, out TVerificationResultCollection AVerifications)
        {
            AVerifications = new TVerificationResultCollection();
            //For use in transaction delegate
            TVerificationResultCollection VerificationResult = AVerifications;
            TVerificationResultCollection SingleVerificationResultCollection;

            //Error handling
            string ErrorContext = "Posting a Gift Batch";
            string ErrorMessage = String.Empty;
            TResultSeverity ErrorType = TResultSeverity.Resv_Noncritical;

            //TVerificationResultCollection VerificationResult = null;

            TDBTransaction Transaction = null;
            bool SubmissionOK = false;

            TProgressTracker.InitProgressTracker(DomainManager.GClientID.ToString(),
                Catalog.GetString("Posting gift batches"),
                ABatchNumbers.Count * 3 + 1);

            DBAccess.GDBAccessObj.BeginAutoTransaction(IsolationLevel.Serializable,
                ref Transaction,
                ref SubmissionOK,
                delegate
                {
                    List <Int32>GLBatchNumbers = new List <int>();

                    try
                    {
                        // first prepare all the gift batches, mark them as posted, and create the GL batches
                        foreach (Int32 BatchNumber in ABatchNumbers)
                        {
                            TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                                Catalog.GetString("Posting gift batches"),
                                ABatchNumbers.IndexOf(BatchNumber) * 3);

                            GiftBatchTDS MainDS = PrepareGiftBatchForPosting(ALedgerNumber,
                                BatchNumber,
                                ref Transaction,
                                out SingleVerificationResultCollection);

                            VerificationResult.AddCollection(SingleVerificationResultCollection);

                            if (MainDS == null)
                            {
                                return;
                            }

                            TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                                Catalog.GetString("Posting gift batches"),
                                ABatchNumbers.IndexOf(BatchNumber) * 3 + 1);

                            // create GL batch
                            GLBatchTDS GLDataset = CreateGLBatchAndTransactionsForPostingGifts(ALedgerNumber, ref MainDS);

                            ABatchRow batch = GLDataset.ABatch[0];

                            // it is possible that gl transactions are not actually needed for a gift posting.
                            // E.g. it is only a donor name adjustment -- there is no change in the general ledger account.
                            bool GLBatchNotRequired = GLDataset.ATransaction.Count == 0;

                            if (GLBatchNotRequired)
                            {
                                TGLPosting.DeleteGLBatch(ALedgerNumber, batch.BatchNumber, out SingleVerificationResultCollection);
                                VerificationResult.AddCollection(SingleVerificationResultCollection);
                            }

                            // save the batch (or delete if it is not actually needed)
                            if (GLBatchNotRequired || (TGLTransactionWebConnector.SaveGLBatchTDS(ref GLDataset,
                                                           out SingleVerificationResultCollection) == TSubmitChangesResult.scrOK))
                            {
                                if (!GLBatchNotRequired)
                                {
                                    VerificationResult.AddCollection(SingleVerificationResultCollection);
                                    GLBatchNumbers.Add(batch.BatchNumber);
                                }

                                //
                                //                     Assign ReceiptNumbers to Gifts
                                //
                                ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);
                                Int32 LastReceiptNumber = MainDS.ALedger[0].LastHeaderRNumber;

                                foreach (AGiftRow GiftRow in MainDS.AGift.Rows)
                                {
                                    LastReceiptNumber++;
                                    GiftRow.ReceiptNumber = LastReceiptNumber;
                                }

                                MainDS.ALedger[0].LastHeaderRNumber = LastReceiptNumber;

                                //Mark gift batch as posted
                                MainDS.AGiftBatch[0].BatchStatus = MFinanceConstants.BATCH_POSTED;

                                MainDS.ThrowAwayAfterSubmitChanges = true;
                                GiftBatchTDSAccess.SubmitChanges(MainDS);
                            }
                            else
                            {
                                VerificationResult.AddCollection(SingleVerificationResultCollection);
                                return;
                            }
                        }

                        TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                            Catalog.GetString("Posting gift batches"),
                            ABatchNumbers.Count * 3 - 1);

                        // now post the GL batches
                        if (!TGLPosting.PostGLBatches(ALedgerNumber, GLBatchNumbers,
                                out SingleVerificationResultCollection))
                        {
                            VerificationResult.AddCollection(SingleVerificationResultCollection);
                            // Transaction will be rolled back, no open GL batch flying around
                            SubmissionOK = false;
                        }
                        else
                        {
                            VerificationResult.AddCollection(SingleVerificationResultCollection);
                            SubmissionOK = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage =
                            String.Format(Catalog.GetString("Unknown error while posting Gift batch." +
                                    Environment.NewLine + Environment.NewLine + ex.ToString()),
                                ALedgerNumber);
                        ErrorType = TResultSeverity.Resv_Critical;

                        VerificationResult = new TVerificationResultCollection();
                        VerificationResult.Add(new TVerificationResult(ErrorContext, ErrorMessage, ErrorType));

                        throw new EVerificationResultsException(ErrorMessage, VerificationResult, ex.InnerException);
                    }
                });

            TProgressTracker.FinishJob(DomainManager.GClientID.ToString());

            AVerifications = VerificationResult;

            return SubmissionOK;
        }

        /// <summary>
        /// export all the Data of the batches matching the parameters to a String
        /// </summary>
        /// <param name="requestParams">Hashtable containing the given params </param>
        /// <param name="exportString">Big parts of the export file as a simple String</param>
        /// <param name="AMessages">Additional messages to display in a messagebox</param>
        /// <returns>number of exported batches</returns>
        [RequireModulePermission("FINANCE-1")]
        static public Int32 ExportAllGiftBatchData(
            Hashtable requestParams,
            out String exportString,
            out TVerificationResultCollection AMessages)
        {
            TGiftExporting exporting = new TGiftExporting();

            return exporting.ExportAllGiftBatchData(requestParams, out exportString, out AMessages);
        }

        /// <summary>
        /// Import Gift batch data
        /// The data file contents from the client is sent as a string, imported in the database
        /// and committed immediately
        /// </summary>
        /// <param name="requestParams">Hashtable containing the given params </param>
        /// <param name="importString">The import file as a simple String</param>
        /// <param name="ANeedRecipientLedgerNumber">Gifts in this table are responsible for failing the
        /// import becuase their Family recipients do not have an active Gift Destination</param>
        /// <param name="AMessages">Additional messages to display in a messagebox</param>
        /// <returns>false if error</returns>
        [RequireModulePermission("FINANCE-1")]
        public static bool ImportGiftBatches(
            Hashtable requestParams,
            String importString,
            out GiftBatchTDSAGiftDetailTable ANeedRecipientLedgerNumber,
            out TVerificationResultCollection AMessages
            )
        {
            TGiftImporting importing = new TGiftImporting();

            return importing.ImportGiftBatches(requestParams, importString, out ANeedRecipientLedgerNumber, out AMessages);
        }

        /// <summary>
        /// Import Gift batch transactions only into an existing batch
        /// The data file contents from the client is sent as a string, imported in the database
        /// and committed immediately
        /// </summary>
        /// <param name="requestParams">Hashtable containing the given params </param>
        /// <param name="importString">The import file as a simple String</param>
        /// <param name="AGiftBatchNumber">The gift batch number into which the transactions will be imported</param>
        /// <param name="ANeedRecipientLedgerNumber">Gifts in this table are responsible for failing the
        /// import becuase their Family recipients do not have an active Gift Destination</param>
        /// <param name="AMessages">Additional messages to display in a messagebox</param>
        /// <returns>false if error</returns>
        [RequireModulePermission("FINANCE-1")]
        public static bool ImportGiftTransactions(
            Hashtable requestParams,
            String importString,
            Int32 AGiftBatchNumber,
            out GiftBatchTDSAGiftDetailTable ANeedRecipientLedgerNumber,
            out TVerificationResultCollection AMessages
            )
        {
            TGiftImporting importing = new TGiftImporting();

            return importing.ImportGiftTransactions(requestParams, importString, AGiftBatchNumber, out ANeedRecipientLedgerNumber, out AMessages);
        }

        /// <summary>
        /// Load Partner Data
        /// </summary>
        /// <param name="PartnerKey">Partner Key </param>
        /// <returns>Partnertable for the partner Key</returns>
        [RequireModulePermission("FINANCE-1")]
        public static PPartnerTable LoadPartnerData(long PartnerKey)
        {
            PPartnerTable PartnerTbl = null;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    PartnerTbl = PPartnerAccess.LoadByPrimaryKey(PartnerKey, Transaction);
                });

            return PartnerTbl;
        }

        /// <summary>
        /// Load Donor Banking Details
        /// </summary>
        /// <param name="APartnerKey">Partner Key </param>
        /// <param name="ABankingDetailsKey">Banking Details Key Key </param>
        /// <returns>Partnertable for the partner Key</returns>
        [RequireModulePermission("FINANCE-1")]
        public static PBankingDetailsTable GetDonorBankingDetails(long APartnerKey, int ABankingDetailsKey = 0)
        {
            PBankingDetailsTable ReturnValue = null;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    if (ABankingDetailsKey == 0)
                    {
                        PBankingDetailsTable BankingDetailsTable =
                            PBankingDetailsAccess.LoadViaPPartner(APartnerKey, Transaction);

                        // Find partner's 'main' bank account
                        foreach (PBankingDetailsRow Row in BankingDetailsTable.Rows)
                        {
                            if (PBankingDetailsUsageAccess.Exists(APartnerKey, Row.BankingDetailsKey, "MAIN", Transaction))
                            {
                                ReturnValue = new PBankingDetailsTable();
                                ReturnValue.Rows.Add((object[])Row.ItemArray.Clone());
                                break;
                            }
                        }
                    }
                    else
                    {
                        ReturnValue = PBankingDetailsAccess.LoadByPrimaryKey(ABankingDetailsKey, Transaction);
                    }
                });

            return ReturnValue;
        }

        /// <summary>
        /// Load Partner Tax Deductible Pct
        /// </summary>
        /// <param name="PartnerKey">Partner Key </param>
        /// <returns>PPartnerTaxDeductiblePctTable for the partner Key</returns>
        [RequireModulePermission("FINANCE-1")]
        public static PPartnerTaxDeductiblePctTable LoadPartnerTaxDeductiblePct(long PartnerKey)
        {
            PPartnerTaxDeductiblePctTable PartnerTaxDeductiblePct = null;

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.BeginAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    PartnerTaxDeductiblePct = PPartnerTaxDeductiblePctAccess.LoadViaPPartner(PartnerKey, Transaction);
                });

            PartnerTaxDeductiblePct.AcceptChanges();

            return PartnerTaxDeductiblePct;
        }

        /// <summary>
        /// Find the cost centre associated with the partner
        /// </summary>
        /// <returns>Cost Centre code</returns>
        [RequireModulePermission("FINANCE-1")]
        public static string IdentifyPartnerCostCentre(Int32 ALedgerNumber, Int64 AFieldNumber)
        {
            TCacheable CachePopulator = new TCacheable();
            Type typeOfTable;
            AValidLedgerNumberTable ValidLedgerNumbers = (AValidLedgerNumberTable)
                                                         CachePopulator.GetCacheableTable(TCacheableFinanceTablesEnum.ValidLedgerNumberList,
                "",
                false,
                out typeOfTable);

            AValidLedgerNumberRow ValidLedgerNumberRow = (AValidLedgerNumberRow)
                                                         ValidLedgerNumbers.Rows.Find(new object[] { ALedgerNumber, AFieldNumber });

            if (ValidLedgerNumberRow != null)
            {
                return ValidLedgerNumberRow.CostCentreCode;
            }
            else
            {
                return TGLTransactionWebConnector.GetStandardCostCentre(ALedgerNumber);
            }
        }

        /// <summary>
        /// get the recipient ledger partner for a unit or the gift destination for a family
        /// </summary>
        /// <param name="APartnerKey"></param>
        /// <param name="AGiftDate">Gift Date (needed for getting a family's Gift Destination)</param>
        /// <returns></returns>
        [RequireModulePermission("FINANCE-1")]
        public static Int64 GetRecipientFundNumber(Int64 APartnerKey, DateTime? AGiftDate = null)
        {
            bool DataLoaded = false;

            GiftBatchTDS MainDS = new GiftBatchTDS();

            TDBTransaction Transaction = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    try
                    {
                        MainDS.LedgerPartnerTypes.Merge(PPartnerTypeAccess.LoadViaPType(MPartnerConstants.PARTNERTYPE_LEDGER, Transaction));
                        MainDS.RecipientPartners.Merge(PPartnerAccess.LoadByPrimaryKey(APartnerKey, Transaction));
                        MainDS.RecipientFamily.Merge(PFamilyAccess.LoadByPrimaryKey(APartnerKey, Transaction));
                        MainDS.RecipientPerson.Merge(PPersonAccess.LoadByPrimaryKey(APartnerKey, Transaction));
                        MainDS.RecipientUnit.Merge(PUnitAccess.LoadByPrimaryKey(APartnerKey, Transaction));
                        DataLoaded = true;
                    }
                    catch (Exception)
                    {
                        TLogging.Log(String.Format(Catalog.GetString("Error in getting recipient fund number for PartnerKey {0}"), APartnerKey));
                    }
                });

            if (DataLoaded)
            {
                return GetRecipientFundNumberInner(MainDS, APartnerKey, AGiftDate);
            }
            else
            {
                return 0;
            }
        }

        private static Int64 GetRecipientFundNumberInner(GiftBatchTDS AMainDS, Int64 APartnerKey, DateTime? AGiftDate = null)
        {
            TDBTransaction Transaction = null;

            if (APartnerKey == 0)
            {
                return 0;
            }

            //Look in RecipientFamily table
            PFamilyRow familyRow = (PFamilyRow)AMainDS.RecipientFamily.Rows.Find(APartnerKey);

            if (familyRow != null)
            {
                return GetGiftDestinationForRecipient(APartnerKey, AGiftDate);
            }

            //Look in RecipientPerson table
            PPersonRow personRow = (PPersonRow)AMainDS.RecipientPerson.Rows.Find(APartnerKey);

            if (personRow != null)
            {
                return GetGiftDestinationForRecipient(personRow.FamilyKey, AGiftDate);
            }

            //Check that LedgerPartnertypes are already loaded
            if (AMainDS.LedgerPartnerTypes.Count == 0)
            {
                PPartnerTypeTable PPTTable = null;

                DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                    TEnforceIsolationLevel.eilMinimum,
                    ref Transaction,
                    delegate
                    {
                        PPTTable = PPartnerTypeAccess.LoadViaPType(MPartnerConstants.PARTNERTYPE_LEDGER, Transaction);
                    });

                AMainDS.LedgerPartnerTypes.Merge(PPTTable);
            }

            if (AMainDS.LedgerPartnerTypes.Rows.Find(new object[] { APartnerKey, MPartnerConstants.PARTNERTYPE_LEDGER }) != null)
            {
                //TODO Warning on inactive Fund from p_partner table
                return APartnerKey;
            }

            UmUnitStructureTable UnitStructTbl = null;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    UnitStructTbl = UmUnitStructureAccess.LoadViaPUnitChildUnitKey(APartnerKey, Transaction);
                });

            if (UnitStructTbl.Rows.Count > 0)
            {
                UmUnitStructureRow structureRow = UnitStructTbl[0];

                if (structureRow.ParentUnitKey == structureRow.ChildUnitKey)
                {
                    // should not get here
                    TLogging.Log("GetRecipientFundNumberInner: - should not get here");
                    return 0;
                }

                // recursive call until we find a partner that has partnertype LEDGER
                return GetRecipientFundNumberInner(AMainDS, structureRow.ParentUnitKey);
            }
            else
            {
                return APartnerKey;
            }
        }

        /// <summary>
        /// Check if Key Ministry exists
        /// </summary>
        /// <param name="APartnerKey">Partner Key </param>
        /// <param name="AIsActive">return true if Key Ministry is active </param>
        /// <returns>return true if APartnerKey identifies a Key Ministry</returns>
        [RequireModulePermission("FINANCE-1")]
        public static Boolean KeyMinistryExists(Int64 APartnerKey, out Boolean AIsActive)
        {
            Boolean KeyMinistryExists = false;
            TDBTransaction Transaction = null;

            bool IsActive = false;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    PUnitTable UnitTable = PUnitAccess.LoadByPrimaryKey(APartnerKey, Transaction);

                    if (UnitTable.Rows.Count == 1)
                    {
                        // this partner is indeed a unit
                        PUnitRow UnitRow = UnitTable[0];

                        if (UnitRow.UnitTypeCode.Equals(MPartnerConstants.UNIT_TYPE_KEYMIN))
                        {
                            KeyMinistryExists = true;

                            PPartnerTable PartnerTable = PPartnerAccess.LoadByPrimaryKey(APartnerKey, Transaction);
                            PPartnerRow PartnerRow = PartnerTable[0];

                            if (SharedTypes.StdPartnerStatusCodeStringToEnum(PartnerRow.StatusCode) == TStdPartnerStatusCode.spscACTIVE)
                            {
                                IsActive = true;
                            }
                        }
                    }
                });

            AIsActive = IsActive;

            return KeyMinistryExists;
        }

        /// <summary>
        /// Check if Key Ministry exists
        /// </summary>
        /// <param name="AKeyMinPartnerKey">Partner Key </param>
        /// <returns>return true if AKeyMinPartnerKey identifies an active Key Ministry</returns>
        [RequireModulePermission("FINANCE-1")]
        public static Boolean KeyMinistryIsActive(Int64 AKeyMinPartnerKey)
        {
            Boolean KeyMinistryIsActive = false;
            TDBTransaction Transaction = null;

            try
            {
                Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.ReadCommitted);

                PPartnerTable PartnerTable = PPartnerAccess.LoadByPrimaryKey(AKeyMinPartnerKey, Transaction);
                PPartnerRow PartnerRow = PartnerTable[0];

                KeyMinistryIsActive = (SharedTypes.StdPartnerStatusCodeStringToEnum(PartnerRow.StatusCode) == TStdPartnerStatusCode.spscACTIVE);
            }
            finally
            {
                if (Transaction != null)
                {
                    DBAccess.GDBAccessObj.RollbackTransaction();
                }
            }

            return KeyMinistryIsActive;
        }

        /// <summary>
        /// Load key Ministry
        /// </summary>
        /// <param name="APartnerKey">Partner Key </param>
        /// <param name="AFieldNumber">Field Number </param>
        /// <returns>ArrayList for loading the key ministry combobox</returns>
        [RequireModulePermission("FINANCE-1")]
        public static PUnitTable LoadKeyMinistry(Int64 APartnerKey, out Int64 AFieldNumber)
        {
            AFieldNumber = 0;
            Int64 FieldNumber = AFieldNumber;

            PUnitTable UnitTable = null;

            TDBTransaction Transaction = null;
            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                ref Transaction,
                delegate
                {
                    UnitTable = LoadKeyMinistries(APartnerKey, Transaction);
                    FieldNumber = GetRecipientFundNumber(APartnerKey);
                });

            AFieldNumber = FieldNumber;

            UnitTable.AcceptChanges();

            return UnitTable;
        }

        /// <summary>
        /// get the key ministries. If Recipient is a field, get the key ministries of that field.
        /// If Recipient is a key ministry itself, get all key ministries of the same field
        /// </summary>
        private static PUnitTable LoadKeyMinistries(Int64 ARecipientPartnerKey, TDBTransaction ATransaction)
        {
            PUnitTable UnitTable = PUnitAccess.LoadByPrimaryKey(ARecipientPartnerKey, ATransaction);

            if (UnitTable.Rows.Count == 1)
            {
                // this partner is indeed a unit
                PUnitRow unitRow = UnitTable[0];

                switch (unitRow.UnitTypeCode)
                {
                    case MPartnerConstants.UNIT_TYPE_KEYMIN:
                        Int64 fieldNumber = GetRecipientFundNumber(ARecipientPartnerKey);
                        UnitTable = LoadKeyMinistriesOfField(fieldNumber, ATransaction);
                        break;

                    case MPartnerConstants.UNIT_TYPE_FIELD:
                        UnitTable = LoadKeyMinistriesOfField(ARecipientPartnerKey, ATransaction);
                        break;
                }
            }

            return UnitTable;
        }

        private static PUnitTable LoadKeyMinistriesOfField(Int64 partnerKey, TDBTransaction ATransaction)
        {
            string sqlLoadKeyMinistriesOfField =
                "SELECT unit.* FROM PUB_um_unit_structure us, PUB_p_unit unit, PUB_p_partner partner " +
                "WHERE us.um_parent_unit_key_n = " + partnerKey.ToString() + " " +
                "AND unit.p_partner_key_n = us.um_child_unit_key_n " +
                "AND unit.u_unit_type_code_c = '" + MPartnerConstants.UNIT_TYPE_KEYMIN + "' " +
                "AND partner.p_partner_key_n = unit.p_partner_key_n " +
                "AND partner.p_status_code_c = '" + MPartnerConstants.PARTNERSTATUS_ACTIVE + "'";

            PUnitTable UnitTable = new PUnitTable();

            DBAccess.GDBAccessObj.SelectDT(UnitTable, sqlLoadKeyMinistriesOfField, ATransaction, new OdbcParameter[0], 0, 0);

            return UnitTable;
        }

        /// <summary>
        /// Load Inactive Key Ministries Found In Batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AInactiveKMsTable"></param>
        /// <returns>Return true if inactive ones found</returns>
        [RequireModulePermission("FINANCE-1")]
        public static bool InactiveKeyMinistriesFoundInBatch(Int32 ALedgerNumber, Int32 ABatchNumber, out DataTable AInactiveKMsTable)
        {
            TDBTransaction Transaction = null;

            AInactiveKMsTable = new DataTable();
            AInactiveKMsTable.Columns.Add(new DataColumn(AGiftDetailTable.GetGiftTransactionNumberDBName(), typeof(Int32)));
            AInactiveKMsTable.Columns.Add(new DataColumn(AGiftDetailTable.GetDetailNumberDBName(), typeof(Int32)));
            AInactiveKMsTable.Columns.Add(new DataColumn(AGiftDetailTable.GetRecipientKeyDBName(), typeof(Int64)));
            AInactiveKMsTable.Columns.Add(new DataColumn(PUnitTable.GetUnitNameDBName(), typeof(String)));

            DataTable InactiveKMsTable = AInactiveKMsTable;

            string SQLLoadInactiveKeyMinistriesInBatch = string.Empty;

            DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                TEnforceIsolationLevel.eilMinimum,
                ref Transaction,
                delegate
                {
                    SQLLoadInactiveKeyMinistriesInBatch =
                        "SELECT gd.a_gift_transaction_number_i, a_detail_number_i, p_recipient_key_n, unit.p_unit_name_c" +
                        " FROM a_gift_detail gd, um_unit_structure us, p_unit unit, p_partner partner" +
                        " WHERE gd.p_recipient_key_n = partner.p_partner_key_n" +
                        "   AND gd.a_ledger_number_i = " + ALedgerNumber.ToString() +
                        "   AND gd.a_batch_number_i = " + ABatchNumber.ToString() +
                        "   AND gd.a_gift_transaction_amount_n > 0" +
                        "   AND partner.p_partner_key_n = unit.p_partner_key_n" +
                        "   AND partner.p_status_code_c = '" + MPartnerConstants.PARTNERSTATUS_INACTIVE + "'" +
                        "   AND unit.p_partner_key_n = us.um_child_unit_key_n" +
                        "   AND unit.u_unit_type_code_c = '" + MPartnerConstants.UNIT_TYPE_KEYMIN + "';";

                    DBAccess.GDBAccessObj.SelectDT(InactiveKMsTable, SQLLoadInactiveKeyMinistriesInBatch, Transaction, new OdbcParameter[0], 0, 0);
                });

            return AInactiveKMsTable.Rows.Count > 0;
        }

        #region Data Validation

        static partial void ValidateGiftBatch(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);
        static partial void ValidateGiftBatchManual(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);
        static partial void ValidateGiftDetail(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);
        static partial void ValidateGiftDetailManual(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);

        static partial void ValidateRecurringGiftBatch(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);
        static partial void ValidateRecurringGiftBatchManual(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);
        static partial void ValidateRecurringGiftDetail(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);
        static partial void ValidateRecurringGiftDetailManual(ref TVerificationResultCollection AVerificationResult, TTypedDataTable ASubmitTable);

        #endregion Data Validation
    }
}