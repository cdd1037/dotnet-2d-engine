#ifndef GAL_UI_MODEL_H
#define GAL_UI_MODEL_H
#include "gal.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Generic copied data model v1. No pointers or callbacks retained. Main thread,
   outside frames. Schemas: preorder, root record, parent UINT32_MAX; <=128 nodes,
   depth <=16, array limit <=64. Snapshots: preorder, <=2048 nodes, text <=255 UTF8.
   Keys are exact uint64, exposed to expressions as decimal strings, never doubles. */
enum { GAL_DATA_TEXT=1,GAL_DATA_BOOL=2,GAL_DATA_NUMBER=3,GAL_DATA_KEY=4,GAL_DATA_RECORD=5,GAL_DATA_ARRAY=6 };
typedef struct { uint32_t kind,parent,limit,reserved; char name[48]; } gal_ui_data_schema;
typedef struct { uint32_t schema,children,reserved,flags; double number; uint64_t key; char text[256]; } gal_ui_data_value;
typedef struct { uint32_t id,count,kinds[4]; char name[48]; } gal_ui_command;
typedef struct { uint32_t size,version,generation,revision,count,reserved; } gal_ui_data_snapshot;
typedef struct { uint32_t kind,reserved; double number; uint64_t key; char text[256]; } gal_ui_argument;
typedef struct { uint32_t size,generation,revision,command,count,reserved; gal_ui_argument arguments[4]; } gal_ui_event;
GAL_API int GAL_CALL gal_ui_model_open(gal_context*,const char* path,const char* font,const char* stylesheet,const gal_ui_data_schema*,uint32_t schema_count,const gal_ui_command*,uint32_t command_count,const char*const* images,uint32_t image_count);
/* Additive initialized staging. Snapshot generation must be 0 and revision 1.
   Copies source/schema/initial values into one candidate; never draws. The next
   normal frame validates the candidate renderer before replacing the live document.
   State.pending reports staging, State.loaded/generation retain the old live document.
   Deferred failure clears pending and records a diagnostic while retaining live UI. */
GAL_API int GAL_CALL gal_ui_model_stage(gal_context*,const char* path,const char* font,const char* stylesheet,const gal_ui_data_schema*,uint32_t schema_count,const gal_ui_command*,uint32_t command_count,const char*const* images,uint32_t image_count,const gal_ui_data_snapshot*,const gal_ui_data_value*);
GAL_API int GAL_CALL gal_ui_model_apply(gal_context*,const gal_ui_data_snapshot*,const gal_ui_data_value*);
GAL_API int GAL_CALL gal_ui_model_poll(gal_context*,gal_ui_event*);
/* Probe-only exact ID + occurrence among cloned nodes. 1=dispatch click, 2=hit test
   click, 3=pointer down, 4=pointer up, 5=read text, 6=set value/change, 7=focus, 8=queued preedit, 9=queued CJK commit, 10=scroll into view,
   11=caret to end, 12=queued ASCII commit without refocusing,
   13=live+candidate file texture count, 14=reject next candidate render (one-shot fault).
   Packet generation/revision are checked before touching the element. */
GAL_API int GAL_CALL gal_ui_model_test(gal_context*,uint32_t command,const char* id,uint32_t occurrence,gal_ui_event*);
#ifdef __cplusplus
}
#endif
#endif
