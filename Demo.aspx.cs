using System.Web.UI.WebControls;

namespace ISAP.Frontend.Pages_Production
{
    public partial class ARContainerManagement : System.Web.UI.Page
    {
        #region Consts

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
            btnSendFiles.Visible = false;
            btnResendFiles.Visible = false;

            if (User.Identity.Name.ToLower() != "choef")
            {
                btnGenerateFiles.Visible = false;
            }

            if (!IsPostBack)
            {
                LoadInitialContainer();
            }
            else
            {
                currentId = ViewState["currentId"] as int?;
                currentState = ViewState["currentState"] as int?;
                currentContainerNumber = ViewState["CurrentContainerNumber"] as string;
                previousId = ViewState["previousId"] as int?;
                nextId = ViewState["nextId"] as int?;
            }
        }
        

        private void LoadInitialContainer()
        {
            var container = ppdao.GetLastContainerForTruckingCompanyCode(SapCommons.SapBusinessCommons.ANTON_RÖHR_SHIPPING_COMPANY_CODE);
        
            if (container != null)
            {
                LoadContainer(container.Id);

                ViewState["CurrentContainerNumber"] = container.Sendungsnummer;
                ViewState["CurrentContainerCreationDate"] = container.Erstellungsdatum;
                ViewState["CurrentContainerClosingDate"] = container.Abschlussdatum;
            }
            else
            {
                lblContainer.Text = $"Container: Kein aktuell offener Container";
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
            ViewState["currentId"] = containerId;
            ViewState["CurrentContainerNumber"] = currentContainerNumber = container.Sendungsnummer;
            ViewState["currentState"] = currentState = container.Status;
            ViewState["previousId"] = previousId;
            ViewState["nextId"] = nextId;
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

            if (previousId == null)
                btnPrevious.Enabled = false;
            else
                btnPrevious.Enabled = true;

            if (nextId == null)
                btnNext.Enabled = false;
            else
                btnNext.Enabled = true;

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
            bool isFirstRowOfSalesOrderNumber = false;

            foreach (ProductionItem productionItem in items.OrderByDescending(x => x.SalesOrderNumber))
            {
                SapPreliminaryDeliveryNoteData deliveryNoteData = deliveryNoteDatas.Where(d => d.SalesOrderNumber == productionItem.SalesOrderNumber).FirstOrDefault();
                SapItemForContainerManagement sapItem = sapItems.Where(s => s.ItemCode == productionItem.ArticleCode).FirstOrDefault();
                
                var itemType = MattressModelRecognition.GetProductType(productionItem.ArticleName);
                bool isPillowOrPad = itemType == MattressModelRecognition.ProductTypes.PillowModel ||
                                             (sapItem?.U_Modell ?? "").ToLower().Contains("schulter") ||
                                             (sapItem?.U_Modell ?? "").ToLower().Contains("kopf") ||
                                             (sapItem?.U_Modell2 ?? "").ToLower().Contains("schulter") ||
                                             (sapItem?.U_Modell2 ?? "").ToLower().Contains("kopf");

                decimal weightTotal = 0m;
                decimal volumeTotal = 0m;

                decimal weightPerItem = sapItems.Where(s => s.ItemCode == productionItem.ArticleCode).FirstOrDefault().WeightForContainer;
                decimal volumePerItem = 0m;
                if (!productionItem.Pallet.IsNullOrEmpty() && productionItem.Pallet.Contains("Roll"))
                    volumePerItem = sapItems.Where(s => s.ItemCode == productionItem.ArticleCode).FirstOrDefault().VolumeRolledForContainer;
                else
                    volumePerItem = sapItems.Where(s => s.ItemCode == productionItem.ArticleCode).FirstOrDefault().VolumeForContainer;

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
                row.Cells.Add(new TableCell { Text = deliveryNoteData.PreliminaryDeliveryNoteNumber });
                row.Cells.Add(new TableCell { Text = productionItem.CommissionNumber });
                row.Cells.Add(new TableCell { Text = productionItem.CommissionName });
                row.Cells.Add(new TableCell { Text = productionItem.ArticleCode });
                row.Cells.Add(new TableCell { Text = productionItem.ArticleName });
                row.Cells.Add(new TableCell { Text = productionItem.ArticleQuantity.ToString() });
                row.Cells.Add(new TableCell { Text = weightTotal.ToString("F2") });
                row.Cells.Add(new TableCell { Text = volumeTotal.ToString("F2") + ((!string.IsNullOrEmpty(productionItem.Pallet) && productionItem.Pallet.Contains("Roll")) ? "R" : "") });
                row.Cells.Add(new TableCell { Text = colliNumber.ToString() });

                if (currentState < 100 && isFirstRowOfSalesOrderNumber)
                {
                    LinkButton link = new LinkButton
                    {
                        Text = "Entnehmen",
                        CommandArgument = productionItem.SalesOrderNumber.ToString(),
                        OnClientClick = $"return confirm('Auftrag {productionItem.SalesOrderNumber} wirklich aus Container {currentContainerNumber} entfernen?');"
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

            TableRow rowSummary = new TableRow { CssClass = ( "summaryRow" ) };

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
            int salesOrderNumber = int.Parse(e.CommandArgument.ToString());

            if (1 == 2) { }
        }

        protected void btnPrevious_Click(object sender, EventArgs e)
        {
            if (previousId != null)
                LoadContainer(previousId.Value);
        }

        protected void btnNext_Click(object sender, EventArgs e)
        {
            if (nextId != null)
                LoadContainer(nextId.Value);
        }

        protected void btnSendFiles_Click(object sender, EventArgs e)
        {
            ScanProductionItems.ProcessAntonRoehrCompletion(User, currentId, true);
            LoadContainer(currentId.Value);
        }

        protected void btnResendFiles_Click(object sender, EventArgs e)
        {
            ScanProductionItems.ProcessAntonRoehrCompletion(User, currentId, false);
            LoadContainer(currentId.Value);
        }

        protected void btnGenerateFiles_Click(object sender, EventArgs e)
        {
            ScanProductionItems.ProcessAntonRoehrCompletion(User, currentId, false);
            LoadContainer(currentId.Value);
        }
    }
}
