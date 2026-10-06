/* RC6.4.5 — navigate between main UI and fullscreen Studio without global UI rewrites. */
(()=>{'use strict';
 const page=document.getElementById('page-trends');
 const frame=document.getElementById('fullscreenTrendFrame');
 if(!page||!frame)return;
 const send=()=>{if(location.origin==='null')return;const visible=page.classList.contains('active');
   frame.contentWindow?.postMessage({type:'pgn:trend-visible',visible},location.origin);
 };
 new MutationObserver(send).observe(page,{attributes:true,attributeFilter:['class']});
 frame.addEventListener('load',()=>{send();
   frame.contentWindow?.postMessage({type:'pgn:access-changed'},location.origin);
 });
 window.addEventListener('prognode:historianchanged',()=>frame.contentWindow?.postMessage({type:'pgn:historian-changed'},location.origin));
 window.addEventListener('prognode:accesschanged',()=>frame.contentWindow?.postMessage({type:'pgn:access-changed'},location.origin));
 send();
})();
