/*
 *	The bridge's books and scrolls, by quality (eighteen.uc's
 *	bridge_documents); the others are Black Gate's own. A document that sets
 *	off talk does so in a script run after it, as book mode lasts to the end
 *	of a usecode run (Exult set_book).
 */

void Book shape#(0x282) () {
	if (event == DOUBLECLICK) {
		var quality = get_item_quality();
		if (quality == LEDGER_QUALITY) {
			showLedger(item);
			gflags[READ_LEDGER] = true;
			return;
		}
		if (quality == LOG_QUALITY) {
			showLog(item);
			gflags[DEN_LOG_READ] = true;
			return;
		}
		if (quality == TALES_QUALITY) {
			showTales(item);
			gflags[READ_TALES] = true;
			return;
		}
	}
	Book.original();
}

void Scroll shape#(0x31D) () {
	if (event == DOUBLECLICK) {
		var quality = get_item_quality();
		var avatar = UI_get_avatar_ref();
		if (quality == PASSENGERS_QUALITY) {
			showPassengers(item);
			if (!gflags[READ_PASSENGERS]) {
				script avatar after 1 ticks call passengersRead;
			}
			return;
		}
		if (quality == RECEIPT_QUALITY) {
			showReceipt(item);
			if (!gflags[MINOC_RECEIPT]) {
				script avatar after 1 ticks call receiptRead;
			}
			return;
		}
		if (quality == TALLY_QUALITY) {
			showTally(item);
			gflags[MINOC_STAGING] = true;
			return;
		}
		if (quality == CHART_QUALITY) {
			showChart(item);
			return;
		}
		if (quality == BILL_QUALITY) {
			showBill(item);
			gflags[DEN_SAW_BILL] = true;
			return;
		}
		if (quality == NOTES_QUALITY) {
			showNotes(item);
			return;
		}
		if (quality == NELSON_NOTE_QUALITY) {
			showNelsonNote(item);
			gflags[READ_NELSON_NOTE] = true;
			return;
		}
		if (quality == LETTER_QUALITY) {
			showLetter(item);
			return;
		}
	}
	Scroll.original();
}
