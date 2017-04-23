using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using neolibs;
using neolibs.Supplier;
using neolibs.ControlUtils;

using MySql.Data;
using MySql.Data.MySqlClient;


// AJTODO: MOQ per supplier
// report as to the total purchasing amount from all suppliers
// custom parts (eg UC503 GPS unit that is bought from Electrocomp)

namespace WindowsFormsApplication1
{
    /// <summary>
    /// Main application display Form 
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>
        /// Application setting struct
        /// </summary>
        public struct TAppSettings
        {
            string m_WorkingFolder;

            /// <summary>
            /// wokring folder for this purchase
            /// </summary>
            public string WorkingFolder
            {
                get 
                {
                    return m_WorkingFolder;
                }
                set
                {
                    m_WorkingFolder = value;
                }
            }        
        }

        /// <summary>
        /// instance of application settings
        /// </summary>
        public TAppSettings AppSettings = new TAppSettings();

        int ErrorCount = 0;

        /// <summary>
        /// Constructor
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }

        private void WriteStatus(string s)
        {
            txtStatus.AppendText(s + Environment.NewLine);
        }

        private void btnAddBOM_Click(object sender, EventArgs e)
        {
            // add a file to the BOM List 
            try
            {
                if (openBOMFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ListViewItem t = lvBOMList.Items.Add(openBOMFileDialog.FileName);
                    t.SubItems.Add("1");
                    
                }
            }
            catch (Exception ex)
            {
                WriteStatus("btnAddBOM_Click ERROR: " + ex.ToString());
            }

        }

        private void btnDeleteBOM_Click(object sender, EventArgs e)
        {
            try
            {
                
                ListView.SelectedListViewItemCollection x = lvBOMList.SelectedItems;

                foreach(ListViewItem t in x)
                {
                    lvBOMList.Items.Remove(t);
                }
            }
            catch (Exception ex)
            {
                WriteStatus("btnDeleteBOM_Click ERROR: " + ex.ToString());
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            try
            {
                lvBOMList.Items.Clear();
            }
            catch (Exception ex)
            {
                WriteStatus("btnDeleteBOM_Click ERROR: " + ex.ToString());
            }
        }

        private void btnLoadBOMList_Click(object sender, EventArgs e)
        {
            try
            {
                if (openBOMListFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    lvBOMList.Items.Clear();
                    ListViewUtils.LoadListview(lvBOMList,openBOMListFileDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                WriteStatus("btnLoadBOMList_Click ERROR: " + ex.ToString());
            }
            
        }

        private void btnSaveBOMList_Click(object sender, EventArgs e)
        {
            try
            {
                saveBOMListFileDialog.FileName = AppSettings.WorkingFolder + @"\default.bomlst";
                if (saveBOMListFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ListViewUtils.SaveListview(lvBOMList,saveBOMListFileDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                WriteStatus("btnLoadBOMList_Click ERROR: " + ex.ToString());
            }
        }

        private struct partspurchasedata 
        {
            public int id;
            public string comment;
            public string digikey_pn;
            public int    digikeymoq;
            public string rs_pn;
            public string rs_url;
            public string mouser_pn;
            public string mouser_url;
            public string farnell_pn;
            public string rfdesign_pn;
            public string mantech_pn;
            public string ottopn;
            public string otherpn;
            public string notes;
        }

        private partspurchasedata GetPurchaseInfo(string part)
        {
            try
            {
                partspurchasedata result = new partspurchasedata();
                bool ok = true;

                string sql = "SELECT * FROM partspurchaseinfo WHERE partdesc = '" + part + "'";
                MySqlCommand cmd = new MySqlCommand(sql, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();

                if (rdr.Read())
                {
                    // AJTODO check for duplicate parts? - maybe no required as database has already got unique field
                    Int32.TryParse(rdr[0].ToString(),out result.id);
                    result.comment     = rdr[1].ToString().Trim();
                    result.digikey_pn  = rdr[2].ToString().Trim();
                    result.digikeymoq = 1;
                    Int32.TryParse(rdr[3].ToString().Trim(),out result.digikeymoq);
                    result.rs_pn       = rdr[4].ToString().Trim();
                    result.rs_url      = rdr[5].ToString().Trim();
                    result.mouser_pn   = rdr[6].ToString().Trim();
                    result.mouser_url  = rdr[7].ToString().Trim();
                    result.farnell_pn  = rdr[8].ToString().Trim();
                    result.rfdesign_pn = rdr[9].ToString().Trim();
                    result.mantech_pn  = rdr[10].ToString().Trim();
                    result.ottopn      = rdr[11].ToString().Trim();
                    result.otherpn     = rdr[12].ToString().Trim();
                    result.notes       = rdr[13].ToString().Trim();
                }
                else
                {
                    ok = false;
                }
                rdr.Close();

                if (!ok) throw new Exception(part + " not found in database");

                return result;
            }
            catch(Exception ex)
            {
                if (PartsToIgnoreList.Contains(part))
                {
                    WriteStatus("Purchase info for " + part + " ignored");
                    throw ex;
                }
                else
                {
                    ErrorCount++;
                    throw ex;
                }
            }
        }

        private struct OutputFields
        {
            public string Comment;
            public string Descr;
            public int    bomqty;
            public string Designators;
            public string Manufacturer;
            public string Manuf_pn;
            public string supplier;
            public string suppliercode;
            public double[] unitprices; // less than 0.0 means (call supplier)
            public double[] extprices; // less than 0.0 means call supplier

        }

        private OutputFields AddPricingInfoToBOMLine(OutputFields o, PricingInfo[] p)
        {
            OutputFields res = o;
            try
            {
                for(int i=0; i<quantities.Count(); i++)
                {
                    foreach(PricingInfo price in p)
                    {
                        if ((quantities[i] >= price.minqty) && (quantities[i] <= price.maxqty))
                        {
                            if (price.CallForPricing)
                            {
                                res.unitprices[i] = -1.0;
                                res.extprices[i] = -1.0;
                            }
                            else
                            {
                                // use this price value
                                res.unitprices[i] = price.DestCost;
                                res.extprices[i] = price.DestCost * res.bomqty;
                            }
                        }
                    }
                }
                return res;
            }
            catch(Exception ex)
            {
                ErrorCount++;
                throw ex;
            }
        }

        private void WriteBOMFile(List<OutputFields> x, string fn)
        {
            System.IO.StreamWriter file = new System.IO.StreamWriter(fn);

            //write headers
            file.Write("Comment\tDescr\tQty\tDesignators\tSupplier\tSupplierCode\tManufacturer\tManuf Partno\t");
            foreach(int q in quantities)
            {
                file.Write("Unit price" + q.ToString() + "\t");
            }
            foreach(int q in quantities)
            {
                file.Write("Ext price" + q.ToString() + "\t");
            }
            file.WriteLine();

            // write bom contents
            foreach(OutputFields f in x)
            {
                file.Write(f.Comment+"\t");
                file.Write(f.Descr+"\t");
                file.Write(f.bomqty.ToString()+"\t");
                file.Write(f.Designators+"\t");
                file.Write(f.supplier+"\t");
                file.Write(f.suppliercode+"\t");
                file.Write(f.Manufacturer+"\t");
                file.Write(f.Manuf_pn+"\t");

                for(int i=0; i<quantities.Count(); i++)
                {
                    if (f.unitprices[i] < 0.0)
                    {
                        file.Write("Call Supplier\t");
                    }
                    else
                    {
                        file.Write(f.unitprices[i] + "\t");
                    }
                }
                for(int i=0; i<quantities.Count(); i++)
                {
                    if (f.extprices[i] < 0.0)
                    {
                        file.Write("Call Supplier\t");
                    }
                    else
                    {
                        file.Write(f.extprices[i] + "\t");
                    }
                }
                file.WriteLine();
            }

            file.Close();
        }

        DigikeyOrder  dk_order;
        RFDesignOrder rfd_order;
        MantechOrder  mt_order;
        OttoOrder     om_order;
        RsOrder       rs_order;
        OtherOrder    other_order;
        MouserOrder   ms_order;

        private void ProcessPart(List<OutputFields> BOMOut, string unique_id, string[] cols, int qty,
            int number_of_parts, string refdesignators)
        {
            try
            {
                partspurchasedata ppd = GetPurchaseInfo(unique_id);
                WriteStatus("Purchase info: id=" + ppd.id.ToString() + ", " + 
                    "comment=" + ppd.comment + ", " +
                    "digikeypn=" + ppd.digikey_pn + ", " +
                    "digikeymoq=" + ppd.digikeymoq.ToString() + ", " +
                    "rs_pn=" + ppd.rs_pn + ", " +
                    "rs_url=" + ppd.rs_url + ", " +
                    "mouser_pn=" + ppd.mouser_pn + ", " +
                    "farnellpn=" + ppd.farnell_pn + ", " +
                    "rfdesignpn=" + ppd.rfdesign_pn + ", " +
                    "mantechpn=" + ppd.mantech_pn + ", " +
                    "ottopn=" + ppd.ottopn + ", " +
                    "otherpn=" + ppd.otherpn + ", " +
                    "notes=" + ppd.notes);

                bool fSupplierFound = false;
                PricingInfo[] dk_prices = new PricingInfo[0];
                PricingInfo[] rfd_prices = new PricingInfo[0];
                PricingInfo[] mt_prices = new PricingInfo[0];
                PricingInfo[] om_prices = new PricingInfo[0];
                PricingInfo[] rs_prices = new PricingInfo[0];
                PricingInfo[] ms_prices = new PricingInfo[0];
                PricingInfo[] other_prices = new PricingInfo[0];

                // **** DIGIKEY *****
                DigikeyTools dkt = new DigikeyTools();

                if (ppd.digikey_pn != "")
                {
                    if (ppd.digikey_pn.Contains("PIC18"))
                    {
                        ppd.digikey_pn = ppd.digikey_pn.Trim();
                    }

                    //dkt.LoadPageData(webBrowser1,ppd.digikey_pn);
                    dkt.LoadPageData(geckoWebBrowser1,ppd.digikey_pn);
                    dk_prices = dkt.GetPricingInfo();

                    WriteStatus("Digikey Prices:");
                    WriteStatus("----------------");
                    if (dk_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in dk_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no digikey prices!");
                    }
                    WriteStatus("*");
                }


                // **** RS COMPONENTS *****
                rswebtools rswt = new rswebtools(ppd.rs_url);
                if (ppd.rs_pn != "")
                {
                    rswt.LoadPageData(ppd.rs_pn);
                    rs_prices = rswt.GetPricingInfo();

                    WriteStatus("RS prices Prices:");
                    WriteStatus("----------------");
                    if (rs_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in rs_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no Rs component prices!");
                    }
                    WriteStatus("*");
                }

                // **** MOUSER *****
                MouserWebtools mswt = new MouserWebtools();
                if (ppd.mouser_pn != "")
                {
                    mswt.LoadPageData(ppd.mouser_pn);
                    ms_prices = mswt.GetPricingInfo();

                    WriteStatus("Mouser Prices:");
                    WriteStatus("----------------");
                    if (ms_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in ms_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no Mouser component prices!");
                    }
                    WriteStatus("*");
                }


                // **** FARNELL *****

                // **** RF Design ****
                RFDesignWebTools rfdwt = new RFDesignWebTools();

                if (ppd.rfdesign_pn != "")
                {
                    rfdwt.LoadPageData(ppd.rfdesign_pn);
                    rfd_prices = rfdwt.GetPricingInfo();

                    WriteStatus("RF Design Prices:");
                    WriteStatus("----------------");
                    if (rfd_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in rfd_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no RF Design prices!");
                    }
                    WriteStatus("*");
                }

                // **** Mantech electronics ****
                MantechWebTools mtwt = new MantechWebTools();

                if (ppd.mantech_pn != "")
                {
                    mtwt.LoadPageData(ppd.mantech_pn);
                    mt_prices = mtwt.GetPricingInfo();

                    WriteStatus("Mantech Prices:");
                    WriteStatus("----------------");
                    if (mt_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in mt_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no Mantech prices!");
                    }
                    WriteStatus("*");
                }

                // **** Otto Marketing ****
                OttoMarketingWebTools omwt = new OttoMarketingWebTools();

                if (ppd.ottopn != "")
                {
                    omwt.LoadPageData(ppd.ottopn);
                    om_prices = omwt.GetPricingInfo();

                    WriteStatus("Otto marketing Prices:");
                    WriteStatus("----------------");
                    if (om_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in om_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no Otto marketing prices!");
                    }
                    WriteStatus("*");
                }

                other_comp_tools ocdbt = new other_comp_tools();

                if (ppd.otherpn != "")
                {
                    ocdbt.LoadPricingInfo(ppd.otherpn,conn);
                    other_prices = ocdbt.GetPricingInfo();

                    WriteStatus("Other sources Prices:");
                    WriteStatus("---------------------");
                    if (other_prices.Count() > 0)
                    {
                        foreach(PricingInfo p in other_prices)
                        {
                            WriteStatus("Price: " + p.SrcCost.ToString("#0.000") + p.srcCurr + "(" + p.DestCost.ToString("#0.000") + p.destCurr + ") (" + p.minqty.ToString() + "-" + p.maxqty.ToString() + ")");
                        }
                        fSupplierFound = true;
                    }
                    else
                    {
                        WriteStatus("INFO: no other sources prices!");
                    }
                    WriteStatus("*");
                }

                if (PartsToIgnoreList.Contains(unique_id)) fSupplierFound = true;

                if (!fSupplierFound) throw new Exception("No supplier found for part (will not be included on BOM!): "+ppd.comment);

                // AJTODO: choose lowest cost supplier
                OutputFields of = new OutputFields();

                of.Comment     = unique_id;
                of.Descr       = cols[1].Replace("\"","").Trim();
                of.bomqty      = qty;
                of.Designators = refdesignators;

                if (ppd.digikey_pn != "")
                {
                    // only add to order if digikey has it!
                    WriteStatus("Using Digikey as supplier");
                    of.Manufacturer = dkt.GetManufacturer();
                    of.Manuf_pn     = dkt.GetManufacturerPartNo();
                    of.supplier = "Digikey";
                    of.suppliercode = ppd.digikey_pn;
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,dk_prices);
                    dk_order.AddToOrder(of.suppliercode,of.Manufacturer,of.Manuf_pn,of.bomqty*number_of_parts,of.Comment,ppd.digikeymoq);
                }
                else if (ppd.rs_pn != "")
                {
                    // add to rs order
                    WriteStatus("Using RS Components as supplier");
                    of.Manufacturer = rswt.GetManufacturer();
                    of.Manuf_pn     = rswt.GetManufacturerPartNo();
                    of.supplier = "RS Components";
                    of.suppliercode = ppd.rs_pn;
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,rs_prices);
                    rs_order.AddToOrder(of.suppliercode,of.bomqty*number_of_parts);
                }
                else if (ppd.rfdesign_pn != "")
                {
                    // add rfdesign order
                    WriteStatus("Using RF Design as supplier");
                    of.Manufacturer = rfdwt.GetManufacturer();
                    of.Manuf_pn     = rfdwt.GetManufacturerPartNo();
                    of.supplier     = "RF Design";
                    of.suppliercode = ppd.rfdesign_pn;
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,rfd_prices);
                    rfd_order.AddToOrder(of.suppliercode,of.bomqty*number_of_parts);
                }
                else if (ppd.mantech_pn != "")
                {
                    // add mantech order
                    WriteStatus("Using Mantech Electronics as supplier");
                    of.Manufacturer = mtwt.GetManufacturer();
                    of.Manuf_pn     = mtwt.GetManufacturerPartNo();
                    of.supplier     = "Mantech Electronics";
                    of.suppliercode = ppd.mantech_pn;
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,mt_prices);
                    mt_order.AddToOrder(of.suppliercode,of.bomqty*number_of_parts);
                }
                else if (ppd.ottopn != "")
                {
                    // add otto marketing order
                    WriteStatus("Using Otto Marketing as supplier");
                    of.Manufacturer = omwt.GetManufacturer();
                    of.Manuf_pn     = omwt.GetManufacturerPartNo();
                    of.supplier     = "Otto Marketing";
                    of.suppliercode = ppd.comment;     // NOTE: we take a shortcut here!
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,om_prices);
                    om_order.AddToOrder(of.suppliercode,of.bomqty*number_of_parts);
                }
                else if (ppd.otherpn != "")
                {
                    // add to other orders
                    WriteStatus("Using other supplier");
                    of.Manufacturer = ocdbt.GetManufacturer();
                    of.Manuf_pn     = ocdbt.GetManufacturerPartNo();
                    of.supplier     = ocdbt.GetSupplier();
                    of.suppliercode = ocdbt.GetSupplierCode();
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,other_prices);
                    other_order.AddToOrder(of.suppliercode,of.bomqty*number_of_parts);
                }
                else if (ppd.mouser_pn != "")
                {
                    // add to mouser orders
                    WriteStatus("Using Mouser as supplier");
                    of.Manufacturer = mswt.GetManufacturer();
                    of.Manuf_pn     = mswt.GetManufacturerPartNo();
                    of.supplier     = "Mouser Electronics";
                    of.suppliercode = ppd.mouser_pn;     // NOTE: we take a shortcut here!
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];
                    of = AddPricingInfoToBOMLine(of,ms_prices);
                    ms_order.AddToOrder(of.suppliercode,of.bomqty*number_of_parts);
                }
                else
                {
                    WriteStatus("Unknown supplier");
                    of.Manufacturer = "Unknown";
                    of.Manuf_pn     = "Unknown";
                    of.supplier     = "Unknown";
                    of.suppliercode = "Unknown";
                    of.unitprices   = new double[quantities.Count()];
                    of.extprices    = new double[quantities.Count()];                                
                }
                
                WriteStatus("Writing output BOM");

                BOMOut.Add(of);
            }
            catch(Exception ex)
            {
                if (PartsToIgnoreList.Contains(unique_id))
                {
                    WriteStatus("Purchase pricing info for "+unique_id+" ignored");
                }
                else
                {
                    ErrorCount++;
                    WriteStatus("ERROR: Cannot get purchase info for " + unique_id + " - " + ex.ToString());
                }
            }                        

        }

        private void ProcessFile(string fn, int number_of_parts)
        {
            try
            {
                int linecount = 0;
                WriteStatus("**** Now scanning BOM file: "+fn);
                WriteStatus("Part count = " + number_of_parts.ToString());

                List<OutputFields> BOMOut = new List<OutputFields>(1000);

                string[] bomlines = System.IO.File.ReadAllLines(fn);

                foreach(string line in bomlines)
                {
                    linecount++;

                    if (linecount > 2)
                    {
                        string[] cols = line.Split(new char[] {'\t'});

                        string unique_id = cols[0].Replace("\"","").Trim();
                        int qty=-1;

                        string qtystr = cols[5].Replace("\"","");
                        Int32.TryParse(qtystr,out qty);
                        
                        string designators = cols[2].Replace("\"","");

                        if (qty < 0) throw new Exception("Could not determine quantity on for this line: "+line);

                        WriteStatus("Ref: " + unique_id + ", qty = " + qty.ToString());

                        ProcessPart(BOMOut,unique_id,cols,qty,number_of_parts,designators);
                    }
                }

                WriteBOMFile(BOMOut,fn+@".costed");
                WriteStatus("***** File " + fn + " successfully scanned.");
                WriteStatus("");
            }
            catch(Exception ex)
            {
                ErrorCount++;
                throw ex;
            }
        }

        private MySqlConnection conn;
        private int[] quantities;
        private string[] PartsToIgnoreList;

        /// <summary>
        /// Run the BM costing utility
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">EventArgs</param>
        private void btnRun_Click(object sender, EventArgs e)
        {
            txtStatus.Text = "";

            // WriteStatus("Internet explorer version" + webBrowser1.Version);

            ErrorCount = 0;
            PartsToIgnoreList = txtIgnoreParts.Text.Split(new char[] {','});
            for(int i=0; i<PartsToIgnoreList.Count(); i++)
            {
                PartsToIgnoreList[i] = PartsToIgnoreList[i].Trim();
                WriteStatus("Ignore part (" + i.ToString() + ") " + PartsToIgnoreList[i]);
            }

            Application.DoEvents();
            try
            {
                if (txtQuantities.Text.Length == 0) throw new Exception("No quantities entered");
                List<int> t = new List<int>(10);
                string[] qtystr = txtQuantities.Text.Split(new char[] {','});

                foreach(string s in qtystr)
                {
                    int x;
                    Int32.TryParse(s,out x);
                    t.Add(x);
                }

                if (t.Count() < 1) throw new Exception("Incorrect or no quantities entered.");

                quantities = t.ToArray();

                string qstr = "Quantities: ";
                foreach(int q in quantities)
                {
                    qstr += q.ToString() + ", ";
                }
                WriteStatus(qstr);
            }
            catch
            {
                WriteStatus("Default quantities: ");
                quantities = new int[] {1, 10, 100, 1000};

                string qstr = "Quantities: ";
                foreach(int q in quantities)
                {
                    qstr += q.ToString() + ", ";
                }
                WriteStatus(qstr);
                    
            }
            dk_order    = new DigikeyOrder();
            rfd_order   = new RFDesignOrder();
            mt_order    = new MantechOrder();
            om_order    = new OttoOrder();
            rs_order    = new RsOrder();
            other_order = new OtherOrder();
            ms_order    = new MouserOrder();

            for(int i=0; i<lvBOMList.Items.Count; i++)
            {
                string fn = lvBOMList.Items[i].Text;
                string partcountstr = lvBOMList.Items[i].SubItems[1].Text;
                int partcount = 1;
                Int32.TryParse(partcountstr,out partcount);

                try
                {
                    neolibs.Currency.AddExhangeRate("USD","ZAR");
                    neolibs.Currency.AddExhangeRate("EUR","ZAR");
                    double test1 = neolibs.Currency.Convert("USD","ZAR",1.0);
                    double test2 = neolibs.Currency.Convert("EUR","ZAR",1.0);

                    WriteStatus("1 USD to ZAR = "+test1.ToString("#0.0000"));
                    WriteStatus("1 EUR to ZAR = "+test2.ToString("#0.0000"));

                    string connStr;
                    
                    if (neolibs.General.Commandline.FindParam("--localhostmysql") != 0)
                    {
                        // command line option to use localhost mysql
                        connStr = "server=localhost;user=root;database=neosystems;port=3306;";
                    }
                    else
                    {
                        // use the neosystems mysql server
                        connStr = "server=10.0.0.5;user=armand;password=godcomplex;database=neosystems;port=3306;";
                    }
                    conn = new MySqlConnection(connStr);
                    conn.Open();

                    string tfn = fn.Trim();
                    if (tfn != "")
                    {
                        ProcessFile(tfn,partcount);
                    }

                    conn.Close();
                }
                catch (Exception ex)
                {
                    ErrorCount++;
                    WriteStatus("btnRun_Click ERROR: " + ex.ToString());
                }
            }
            try
            {
                dk_order.SaveToFile(AppSettings.WorkingFolder + @"\dk_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            try
            {
                rfd_order.SaveToFile(AppSettings.WorkingFolder + @"\rfd_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            try
            {
                mt_order.SaveToFile(AppSettings.WorkingFolder + @"\mt_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            try
            {
                om_order.SaveToFile(AppSettings.WorkingFolder + @"\om_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            try
            {
                rs_order.SaveToFile(AppSettings.WorkingFolder + @"\rs_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            try
            {
                ms_order.SaveToFile(AppSettings.WorkingFolder + @"\mouser_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            try
            {
                other_order.SaveToFile(AppSettings.WorkingFolder + @"\other_order.txt");
            }
            catch
            {
                ErrorCount++;
            }
            if (ErrorCount > 0) 
            {
                WriteStatus("");
                WriteStatus("****************************************************************");
                WriteStatus("There were "+ErrorCount.ToString() + " errors during processing");
                WriteStatus("****************************************************************");
                MessageBox.Show("There were errors during the processing","ERROR!",
                    MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
            else
            {
                WriteStatus("");
                WriteStatus("****************************************************************");
                WriteStatus("Processing was successful without errors");
                WriteStatus("****************************************************************");
                MessageBox.Show("Processing was successful","SUCCESS!",
                    MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            System.IO.File.WriteAllText(AppSettings.WorkingFolder + @"\bomtest.txt", txtStatus.Text);
        }

        void LoadLists()
        {
            // TODO: in future get these from xml setup file
            try
            {
                ListViewUtils.LoadListview(lvBOMList, AppSettings.WorkingFolder + @"\default.bomlst");
            }
            catch
            {
                MessageBox.Show("Could not load bom list", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            try
            {
                txtIgnoreParts.Text = System.IO.File.ReadAllText(AppSettings.WorkingFolder + @"\parts.ignore");
            }
            catch
            {
                MessageBox.Show("Could not load the parts to be ignored list", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            AppSettings.WorkingFolder = neolibs.FileUtils.General.GetUserPath();
            LoadSettings();
            LoadLists();

            //geckoWebBrowser1.Navigate("http://www.wikipedia.org");
            geckoWebBrowser1.Navigate(@"http://www.digikey.com/scripts/DkSearch/dksus.dll?Detail&name=497-14045-ND");            
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
            System.IO.File.WriteAllText(AppSettings.WorkingFolder + @"\parts.ignore", txtIgnoreParts.Text);
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            try
            {                
                ListView.SelectedListViewItemCollection x = lvBOMList.SelectedItems;

                if (x.Count > 0)
                {
                    EditBOMQtyForm f = new EditBOMQtyForm();
                    f.txtFilename.Text = x[0].Text;
                    f.txtQty.Text      = x[0].SubItems[1].Text;

                    if (f.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        x[0].Text = f.txtFilename.Text;
                        x[0].SubItems[1].Text = f.txtQty.Text;
                    }
                    f.Dispose();
                }                
            }
            catch (Exception ex)
            {
                WriteStatus("btnDeleteBOM_Click ERROR: " + ex.ToString());
            }
        }

        private void btnSelectFolder_Click(object sender, EventArgs e)
        {
            // select the working folder
            if (workingfolderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                System.Windows.Forms.DialogResult r =
                    MessageBox.Show("Load files from folder " + workingfolderBrowserDialog.SelectedPath, "Confirm loading", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

                if (r == System.Windows.Forms.DialogResult.Cancel)
                {
                    MessageBox.Show("You have selected cancel. Your working folder will not be changed", "Information",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // if not cancelled then remeber new folder
                AppSettings.WorkingFolder = workingfolderBrowserDialog.SelectedPath;
                if (r == System.Windows.Forms.DialogResult.Yes)
                {
                    SaveSettings();
                    LoadLists();
                }
                else
                {
                    MessageBox.Show("No files loaded from selected working folder.", "Information",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }                
            }
        }

        void LoadSettings()
        {
            if (neolibs.General.Commandline.FindParamExact("/noload") == 0)
	        {
		        neolibs.FileUtils.Xml<TAppSettings>.LoadFromXml(ref AppSettings,String.Concat( Application.UserAppDataPath, @"\settings.xml" ));
		        EffectSettings();
	        }
        }

        void EffectSettings()
        {
	        try
	        {
                tbCurrentWorkingFolder.Text = AppSettings.WorkingFolder;
	        }
	        catch(Exception e)
	        {
		        neolibs.General.Error.Show("Unable to effect settings",e);
	        }
        }


        void SaveSettings()
        {
	        neolibs.FileUtils.Xml<TAppSettings>.SaveToXml(AppSettings,String.Concat( Application.UserAppDataPath, @"\settings.xml" ));
	        EffectSettings();
        }

    }
}
