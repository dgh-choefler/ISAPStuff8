using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace ISAP.Frontend.Pages_Production
{
    public partial class ARContainerManagement : System.Web.UI.Page
    {
        #region Consts

        private const string CurrentIdViewStateKey = "currentId";
        private const string CurrentStateViewStateKey = "currentState";
        private const string CurrentContainerNumberViewStateKey = "CurrentContainerNumber";
        private const string PreviousIdViewStateKey = "previousId";
        private const string NextIdViewStateKey = "nextId";

        #endregion

        #region Vars

        private readonly ProductionPlanningDao ppdao = new ProductionPlanningDao();

        private int? currentId;
        private int? currentState;
        private string? currentContainerNumber;
        private int? previousId;
        private int? nextId;

        #endregion

        protected void Page_Init(object sender, EventArgs e)
        {
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            ResetActionButtons();

            if (!string.Equals(User?.Identity?.Name, "choef", StringComparison.OrdinalIgnoreCase))
            {
                btnGenerateFiles.Visible = false;
            }

            if (!IsPostBack)
            {
                LoadInitialContainer();
                return;
            }

            RestoreContainerStateFromViewState();

            // Dynamic LinkButtons must exist before ASP.NET raises the postback event.
            // Rebuilding the table here lets SalesOrderLink_Command fire on link clicks.
            if (currentId.HasValue)
            {
                LoadContainer(currentId.Value);
            }
            else
            {
                LoadInitialContainer();
            }
        }

        private void ResetActionButtons()
        {
            btnSendFiles.Visible = false;
            btnResendFiles.Visible = false;
        }

        private void RestoreContainerStateFromViewState()
        {
            currentId = GetNullableIntFromViewState(CurrentIdViewStateKey);
            currentState = GetNullableIntFromViewState(CurrentStateViewStateKey);
            currentContainerNumber = ViewState[CurrentContainerNumberViewStateKey] as string;
            previousId = GetNullableIntFromViewState(PreviousIdViewStateKey);
            nextId = GetNullableIntFromViewState(NextIdViewStateKey);
        }

        private int? GetNullableIntFromViewState(string key)
        {
            object value = ViewState[key];

            if (value == null)
            {
                return null;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (value is byte byteValue)
            {
                return byteValue;
            }

            if (value is short shortValue)
            {
                return shortValue;
            }

            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        private void LoadInitialContainer()
        {
            var container = ppdao.GetLastContainerForTruckingCompanyCode(SapCommons.SapBusinessCommons.ANTON_RÖHR_SHIPPING_COMPANY_CODE);

            if (container != null)
            {
                LoadContainer(container.Id);

                ViewState[CurrentContainerNumberViewStateKey] = container.Sendungsnummer;
                ViewState["CurrentContainerCreationDate"] = container.Erstellungsdatum;
                ViewState["CurrentContainerClosingDate"] = container.Abschlussdatum;
            }
            else
            {
                lblContainer.Text = $"Container: Kein aktuell offener Container";
                btnPrevious.Enabled = false;
                btnNext.Enabled = false;
                phTableLeft.Controls.Clear();
            }
        }

        private void LoadContainer(int containerId)
        {
            var container = ppdao.GetContainerInformationForContainerID(containerId);
            var items = ppdao.GetProductionItemsForContainerId(containerId);
            var distinctSalesOrderNumbers = items.Select(x => x.SalesOrderNumber).Distinct().ToList();
            var distinctItemCodes = items.Select(x => x.ArticleCode).Distinct().ToList();
            List<SapItemForContainerManagement> sapItems = SAPDatabaseQueries.Instance.GetItemInfosExtendedForItemCodes(distinctItemCodes);
            List<SapPreliminaryDeliveryNoteData> deliveryNoteDatas = SAPDatabaseQueries.Instance.GetPreliminaryDeliveryNoteForOrderNumbers(distinctSalesOrderNumbers.ToArray());
            (previousId, nextId) = ppdao.GetPreviousAndNextContainerIdForContainerId(containerId, SapCommons.SapBusinessCommons.ANTON_RÖHR_SHIPPING_COMPANY_CODE);

            currentId = containerId;
            currentContainerNumber = container.Sendungsnummer;
            currentState = container.Status;

            ViewState[CurrentIdViewStateKey] = currentId;
            ViewState[CurrentContainerNumberViewStateKey] = currentContainerNumber;
            ViewState[CurrentStateViewStateKey] = currentState;
            ViewState[PreviousIdViewStateKey] = previousId;
            ViewState[NextIdViewStateKey] = nextId;

            string statusText = container.Status == 0 ? "Offen" : container.Status == 50 ? "Geschlossen" : "Abgeschlossen";
            string datumText = container.Erstellungsdatum.ToString("dd.MM.yyyy HH:mm") + " - " + (container.Abschlussdatum.HasValue ? container.Abschlussdatum.Value.ToString("dd.MM.yyyy HH:mm") : "N/A");

            if (container.Status == (byte)ProductionItemShippingInformation.ProductionItemShippingStates.Closed)
            {
                btnSendFiles.Visible = true;
            }
            else if (container.Status == (byte)ProductionItemShippingInformation.ProductionItemShippingStates.Completed)
            {
                btnResendFiles.Visible = true;
            }

            lblContainer.Text = $"Container: {container.Sendungsnummer} ({datumText}) - Status: {statusText}";

            btnPrevious.Enabled = previousId.HasValue;
            btnNext.Enabled = nextId.HasValue;

            GenerateProductionItemsTable(items, sapItems, deliveryNoteDatas);
        }

        private void GenerateProductionItemsTable(List<ProductionItem> items, List<SapItemForContainerManagement> sapItems, List<SapPreliminaryDeliveryNoteData> deliveryNoteDatas)
        {
            Table table = new Table();
            TableHeaderRow header = new TableHeaderRow { CssClass = ("headerRow") };

            header.Cells.Add(new TableHeaderCell { Text = "Auftragsnummer" });
            header.Cells.Add(new TableHeaderCell { Text = "Lieferscheinnummer" });
            header.Cells.Add(new TableHeaderCell { Text = "Kommissionsnummer" });
            header.Cells.Add(new TableHeaderCell { Text = "Kommissionsname" });
            header.Cells.Add(new TableHeaderCell { Text = "Artikelcode" });
            header.Cells.Add(new TableHeaderCell { Text = "Artikelname" });
            header.Cells.Add(new TableHeaderCell { Text = "Menge" });
            header.Cells.Add(new TableHeaderCell { Text = "Gewicht" });
            header.Cells.Add(new TableHeaderCell { Text = "Volumen" });
            header.Cells.Add(new TableHeaderCell { Text = "Anzahl Collis" });
            header.Cells.Add(new TableHeaderCell { Text = "" });

            table.Rows.Add(header);
            bool isAlternateRow = false;

            decimal containerWeightTotal = 0m;
            decimal containerVolumeTotal = 0m;
            int containerAmountTotal = 0;
            int containerCollisTotal = 0;

            int currentSalesOrderNumber = 0;
            bool isFirstRowOfSalesOrderNumber;

            foreach (ProductionItem productionItem in items.OrderByDescending(x => x.SalesOrderNumber))
            {
                SapPreliminaryDeliveryNoteData deliveryNoteData = deliveryNoteDatas.FirstOrDefault(d => d.SalesOrderNumber == productionItem.SalesOrderNumber);
                SapItemForContainerManagement sapItem = sapItems.FirstOrDefault(s => s.ItemCode == productionItem.ArticleCode);

                var itemType = MattressModelRecognition.GetProductType(productionItem.ArticleName);
                bool isPillowOrPad = itemType == MattressModelRecognition.ProductTypes.PillowModel ||
                                             (sapItem?.U_Modell ?? "").ToLower().Contains("schulter") ||
                                             (sapItem?.U_Modell ?? "").ToLower().Contains("kopf") ||
                                             (sapItem?.U_Modell2 ?? "").ToLower().Contains("schulter") ||
                                             (sapItem?.U_Modell2 ?? "").ToLower().Contains("kopf");

                decimal weightTotal = 0m;
                decimal volumeTotal = 0m;

                decimal weightPerItem = sapItem?.WeightForContainer ?? 0m;
                decimal volumePerItem = 0m;
                if (!productionItem.Pallet.IsNullOrEmpty() && productionItem.Pallet.Contains("Roll"))
                    volumePerItem = sapItem?.VolumeRolledForContainer ?? 0m;
                else
                    volumePerItem = sapItem?.VolumeForContainer ?? 0m;

                weightTotal += weightPerItem * productionItem.ArticleQuantity;
                if (!isPillowOrPad)
                    volumeTotal += volumePerItem * productionItem.ArticleQuantity;

                weightTotal = weightTotal / 1000m;
                volumeTotal = volumeTotal / 1000000m;

                int colliNumber = 0;

                if (!isPillowOrPad)
                {
                    colliNumber += productionItem.ScannedBarcodes?.Sum(scan =>
                        string.IsNullOrEmpty(scan.ScannedBarcodes)
                            ? 0
                            : scan.ScannedBarcodes.Count(c => c == '.')
                    ) ?? 0;
                }

                containerWeightTotal += weightTotal;
                containerVolumeTotal += volumeTotal;
                containerAmountTotal += productionItem.ArticleQuantity;
                containerCollisTotal += colliNumber;

                if (currentSalesOrderNumber != productionItem.SalesOrderNumber)
                {
                    isFirstRowOfSalesOrderNumber = true;
                    currentSalesOrderNumber = productionItem.SalesOrderNumber;
                    isAlternateRow = !isAlternateRow;
                }
                else
                {
                    isFirstRowOfSalesOrderNumber = false;
                }

                TableRow row = new TableRow { CssClass = (isAlternateRow ? "altRow" : "") };

                row.Cells.Add(new TableCell { Text = productionItem.SalesOrderNumber.ToString() });
                row.Cells.Add(new TableCell { Text = deliveryNoteData?.PreliminaryDeliveryNoteNumber ?? "" });
                row.Cells.Add(new TableCell { Text = productionItem.CommissionNumber });
                row.Cells.Add(new TableCell { Text = productionItem.CommissionName });
                row.Cells.Add(new TableCell { Text = productionItem.ArticleCode });
                row.Cells.Add(new TableCell { Text = productionItem.ArticleName });
                row.Cells.Add(new TableCell { Text = productionItem.ArticleQuantity.ToString() });
                row.Cells.Add(new TableCell { Text = weightTotal.ToString("F2") });
                row.Cells.Add(new TableCell { Text = volumeTotal.ToString("F2") + ((!string.IsNullOrEmpty(productionItem.Pallet) && productionItem.Pallet.Contains("Roll")) ? "R" : "") });
                row.Cells.Add(new TableCell { Text = colliNumber.ToString() });

                if ((currentState ?? 0) < 100 && isFirstRowOfSalesOrderNumber)
                {
                    LinkButton link = new LinkButton
                    {
                        ID = "RemoveSalesOrder_" + productionItem.SalesOrderNumber.ToString(CultureInfo.InvariantCulture),
                        Text = "Entnehmen",
                        CommandArgument = productionItem.SalesOrderNumber.ToString(CultureInfo.InvariantCulture),
                        OnClientClick = $"return confirm('Auftrag {productionItem.SalesOrderNumber} wirklich aus Container {currentContainerNumber} entfernen?');",
                        CausesValidation = false
                    };

                    link.Command += SalesOrderLink_Command;

                    row.Cells.Add(new TableCell
                    {
                        Controls = { link }
                    });
                }
                else
                {
                    row.Cells.Add(new TableCell());
                }

                table.Rows.Add(row);
            }

            TableRow rowSummary = new TableRow { CssClass = ("summaryRow") };

            rowSummary.Cells.Add(new TableCell { Text = "" });
            rowSummary.Cells.Add(new TableCell { Text = "" });
            rowSummary.Cells.Add(new TableCell { Text = "" });
            rowSummary.Cells.Add(new TableCell { Text = "" });
            rowSummary.Cells.Add(new TableCell { Text = "" });
            rowSummary.Cells.Add(new TableCell { Text = "" });
            rowSummary.Cells.Add(new TableCell { Text = containerAmountTotal.ToString() });
            rowSummary.Cells.Add(new TableCell { Text = containerWeightTotal.ToString("F2") });
            rowSummary.Cells.Add(new TableCell { Text = containerVolumeTotal.ToString("F2") });
            rowSummary.Cells.Add(new TableCell { Text = containerCollisTotal.ToString() });
            rowSummary.Cells.Add(new TableCell { Text = "" });
            table.Rows.Add(rowSummary);

            phTableLeft.Controls.Clear();
            phTableLeft.Controls.Add(table);
        }

        protected void SalesOrderLink_Command(object sender, CommandEventArgs e)
        {
            RestoreContainerStateFromViewState();

            if (!currentId.HasValue)
            {
                ShowClientMessage("Es ist kein Container ausgewählt.");
                LoadInitialContainer();
                return;
            }

            if ((currentState ?? 0) >= 100)
            {
                ShowClientMessage("Aus abgeschlossenen Containern können keine Aufträge entnommen werden.");
                LoadContainer(currentId.Value);
                return;
            }

            if (!int.TryParse(Convert.ToString(e.CommandArgument, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int salesOrderNumber))
            {
                ShowClientMessage("Die Auftragsnummer konnte nicht gelesen werden.");
                LoadContainer(currentId.Value);
                return;
            }

            try
            {
                RemoveSalesOrderFromCurrentContainer(salesOrderNumber);
                LoadContainer(currentId.Value);
                ShowClientMessage($"Auftrag {salesOrderNumber} wurde aus Container {currentContainerNumber} entnommen.");
            }
            catch (Exception ex)
            {
                LoadContainer(currentId.Value);
                ShowClientMessage("Der Auftrag konnte nicht aus dem Container entnommen werden: " + ex.Message);
            }
        }

        private void RemoveSalesOrderFromCurrentContainer(int salesOrderNumber)
        {
            if (!currentId.HasValue)
            {
                throw new InvalidOperationException("Es ist kein Container ausgewählt.");
            }

            string[] candidateNames =
            {
                "RemoveSalesOrderFromContainer",
                "RemoveSalesOrderFromContainerId",
                "RemoveProductionItemsFromContainer",
                "RemoveProductionItemsFromContainerForSalesOrderNumber",
                "RemoveOrderFromContainer",
                "DeleteSalesOrderFromContainer",
                "DeleteProductionItemsFromContainer"
            };

            var methods = ppdao.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => candidateNames.Contains(method.Name, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(method => method.GetParameters().Length);

            foreach (MethodInfo method in methods)
            {
                if (!TryBuildRemoveSalesOrderArguments(method, salesOrderNumber, out object[] arguments))
                {
                    continue;
                }

                try
                {
                    object result = method.Invoke(ppdao, arguments);

                    if (result is bool success && !success)
                    {
                        throw new InvalidOperationException("Die Datenzugriffsmethode meldete keinen Erfolg.");
                    }

                    return;
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
            }

            throw new MissingMethodException(
                "Keine passende Methode im ProductionPlanningDao gefunden. Erwartet wird z.B. RemoveSalesOrderFromContainer(containerId, salesOrderNumber).");
        }

        private bool TryBuildRemoveSalesOrderArguments(MethodInfo method, int salesOrderNumber, out object[] arguments)
        {
            ParameterInfo[] parameters = method.GetParameters();
            arguments = new object[parameters.Length];
            bool mappedSalesOrderNumber = false;
            bool mappedContainer = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                Type parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                string parameterName = parameter.Name ?? "";

                if (parameterType == typeof(int))
                {
                    if (IsSalesOrderParameter(parameterName))
                    {
                        arguments[i] = salesOrderNumber;
                        mappedSalesOrderNumber = true;
                    }
                    else if (IsContainerParameter(parameterName))
                    {
                        arguments[i] = currentId.Value;
                        mappedContainer = true;
                    }
                    else if (parameters.Length == 2 && i == 0)
                    {
                        arguments[i] = currentId.Value;
                        mappedContainer = true;
                    }
                    else if (parameters.Length == 2 && i == 1)
                    {
                        arguments[i] = salesOrderNumber;
                        mappedSalesOrderNumber = true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (parameterType == typeof(string))
                {
                    if (IsContainerParameter(parameterName))
                    {
                        arguments[i] = currentContainerNumber ?? "";
                    }
                    else if (IsUserParameter(parameterName))
                    {
                        arguments[i] = User?.Identity?.Name ?? "";
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (typeof(IPrincipal).IsAssignableFrom(parameterType))
                {
                    arguments[i] = User;
                }
                else if (typeof(IIdentity).IsAssignableFrom(parameterType))
                {
                    arguments[i] = User?.Identity;
                }
                else
                {
                    return false;
                }
            }

            return mappedSalesOrderNumber && mappedContainer;
        }

        private static bool IsSalesOrderParameter(string parameterName)
        {
            return parameterName.IndexOf("sales", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parameterName.IndexOf("order", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parameterName.IndexOf("auftrag", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsContainerParameter(string parameterName)
        {
            return parameterName.IndexOf("container", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parameterName.IndexOf("shipping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parameterName.IndexOf("sendung", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUserParameter(string parameterName)
        {
            return parameterName.IndexOf("user", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parameterName.IndexOf("benutzer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ShowClientMessage(string message)
        {
            string script = "alert('" + HttpUtility.JavaScriptStringEncode(message) + "');";
            ClientScript.RegisterStartupScript(GetType(), "ARContainerManagementMessage", script, true);
        }

        protected void btnPrevious_Click(object sender, EventArgs e)
        {
            if (previousId.HasValue)
                LoadContainer(previousId.Value);
        }

        protected void btnNext_Click(object sender, EventArgs e)
        {
            if (nextId.HasValue)
                LoadContainer(nextId.Value);
        }

        protected void btnSendFiles_Click(object sender, EventArgs e)
        {
            if (!currentId.HasValue)
            {
                ShowClientMessage("Es ist kein Container ausgewählt.");
                return;
            }

            ScanProductionItems.ProcessAntonRoehrCompletion(User, currentId, true);
            LoadContainer(currentId.Value);
        }

        protected void btnResendFiles_Click(object sender, EventArgs e)
        {
            if (!currentId.HasValue)
            {
                ShowClientMessage("Es ist kein Container ausgewählt.");
                return;
            }

            ScanProductionItems.ProcessAntonRoehrCompletion(User, currentId, false);
            LoadContainer(currentId.Value);
        }

        protected void btnGenerateFiles_Click(object sender, EventArgs e)
        {
            if (!currentId.HasValue)
            {
                ShowClientMessage("Es ist kein Container ausgewählt.");
                return;
            }

            ScanProductionItems.ProcessAntonRoehrCompletion(User, currentId, false);
            LoadContainer(currentId.Value);
        }
    }
}
